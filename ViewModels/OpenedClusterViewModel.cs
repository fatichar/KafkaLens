using System.Collections.ObjectModel;
using System.Threading;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using KafkaLens.Shared.Models;
using KafkaLens.Shared;
using KafkaLens.ViewModels.Messages;
using KafkaLens.ViewModels.Services;

namespace KafkaLens.ViewModels;

public partial class OpenedClusterViewModel : ViewModelBase, ITreeNode
{
    private const int SELECTED_ITEM_DELAY_MS = 3;
    private const string KEY_FORMATTER_NAMES_SETTINGS_KEY = "KeyFormatterNames";
    private const string VALUE_FORMATTER_NAMES_SETTINGS_KEY = "ValueFormatterNames";

    private readonly ISettingsService settingsService;
    private readonly ITopicSettingsService topicSettingsService;
    private readonly IMessageSaver messageSaver;
    private readonly IFormatterService formatterService;
    private readonly ClusterViewModel cluster;
    private IKafkaLensClient KafkaLensClient => cluster.Client;

    private static IList<string> FetchPositionsForTopic { get; } = new List<string>();
    private static IList<string> FetchPositionsForPartition { get; } = new List<string>();

    public ITreeNode.NodeType Type => ITreeNode.NodeType.Cluster;

    [ObservableProperty] private bool isSelected;
    [ObservableProperty] private bool isExpanded;
    [ObservableProperty] private string name;
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private bool applyToAllClusters;
    [ObservableProperty] private string? messagesSortColumn;
    [ObservableProperty] private bool? messagesSortAscending;

    [ObservableProperty] private IList<string> formatterNames;
    [ObservableProperty] private IList<string> valueFormatterNames;
    [ObservableProperty] private IList<string> keyFormatterNames;

    [ObservableProperty] private int fetchCount;
    [ObservableProperty] private string? startOffset;
    [ObservableProperty] private bool fetchBackward;
    [ObservableProperty] private string? fetchPosition;
    [ObservableProperty] private DateTime startDate;
    [ObservableProperty] private bool isStartTimeValid = true;

    private TimeOnly startTime;

    public string StartTimeText
    {
        get => field ?? "";
        set
        {
            if (SetProperty(ref field, value))
            {
                IsStartTimeValid = TimeOnly.TryParse(field, out TimeOnly time);
                if (IsStartTimeValid)
                {
                    startTime = time;
                    // Don't immediately reformat - let the user edit naturally
                    // UpdateStartTimeText() will be called when needed (e.g., on focus loss)
                }
            }
        }
    }

    public int FontSize
    {
        get;
        set => SetProperty(ref field, value, true);
    } = 14;

    public IList<string> FetchPositions
    {
        get;
        set => SetProperty(ref field, value);
    }

    public ITreeNode.NodeType SelectedNodeType
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
                OnPropertyChanged(nameof(IsFetchOptionsEnabled));
        }
    } = ITreeNode.NodeType.None;

    public bool IsFetchOptionsEnabled => SelectedNodeType == ITreeNode.NodeType.Topic ||
                                         SelectedNodeType == ITreeNode.NodeType.Partition;
    public bool IsFetchBackwardEnabled => FetchPosition != "Start" && FetchPosition != "End";
    public int[] FetchCounts => settingsService.GetBrowserConfig().FetchCounts.ToArray();

    public ObservableCollection<ITreeNode> Children { get; } = new();
    public ObservableCollection<ITreeNode> Nodes { get; } = new();
    public ObservableCollection<TopicViewModel> Topics { get; } = new();
    public MessagesViewModel CurrentMessages { get; } = new();

    public string Address => cluster.Address;
    public string StatusColor => cluster.StatusColor;
    public bool IsChecking => cluster.IsChecking;
    public string ClusterId => cluster.Id;

    public IList<MessageViewModel> SelectedMessages { get; set; } = new List<MessageViewModel>();
    public bool IsCurrent { get; set; }

    [ObservableProperty] private bool isNavigatorOpen = true;
    [ObservableProperty] private bool isFetchPanelOpen = true;

    public IRelayCommand? CloseCommand { get; set; }

    public RelayCommand ToggleFetchCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public RelayCommand GuessValueFormatterCommand { get; }
    public RelayCommand GuessKeyFormatterCommand { get; }
    public IAsyncRelayCommand SaveTopicSettingsCommand { get; }
    public AsyncRelayCommand SaveSelectedAsRawCommand { get; set; }
    public AsyncRelayCommand SaveSelectedAsFormattedCommand { get; set; }
    public AsyncRelayCommand SaveAllAsRawCommand { get; set; }
    public AsyncRelayCommand SaveAllAsFormattedCommand { get; set; }
    public AsyncRelayCommand CopyKeyCommand { get; }
    public AsyncRelayCommand CopyValueCommand { get; }

    /// <summary>Set by the view to provide clipboard access.</summary>
    public Func<string, Task>? SetClipboardText { get; set; }

    private DateTime StartDateTime => StartDate.Date + startTime.ToTimeSpan();

    static OpenedClusterViewModel()
    {
        FetchPositionsForTopic.Add("End");
        FetchPositionsForTopic.Add("Timestamp");
        FetchPositionsForTopic.Add("Start");

        FetchPositionsForPartition.Add("End");
        FetchPositionsForPartition.Add("Timestamp");
        FetchPositionsForPartition.Add("Offset");
        FetchPositionsForPartition.Add("Start");
    }

    public OpenedClusterViewModel(
        ISettingsService settingsService,
        ITopicSettingsService topicSettingsService,
        IMessageSaver messageSaver,
        IFormatterService formatterService,
        ClusterViewModel cluster,
        string name)
    {
        this.settingsService = settingsService;
        this.topicSettingsService = topicSettingsService;
        this.messageSaver = messageSaver;
        this.formatterService = formatterService;
        this.cluster = cluster;
        this.cluster.PropertyChanged += OnClusterPropertyChanged;
        Name = name;

        var browserConfig = settingsService.GetBrowserConfig();
        FetchCount = browserConfig.DefaultFetchCount;
        FontSize = browserConfig.FontSize;

        ToggleFetchCommand = new RelayCommand(() =>
        {
            if (IsLoading) StopLoading();
            else FetchMessages();
        });
        RefreshCommand = new RelayCommand(() =>
        {
            if (IsLoading) StopLoading();
            FetchMessages();
        });
        GuessValueFormatterCommand = new RelayCommand(() => GuessFormatterForSelectedNode(isKeyFormatter: false));
        GuessKeyFormatterCommand = new RelayCommand(() => GuessFormatterForSelectedNode(isKeyFormatter: true));
        SaveTopicSettingsCommand = new AsyncRelayCommand(SaveTopicSettingsAsync);

        SaveSelectedAsRawCommand = new AsyncRelayCommand(
            () => messageSaver.SaveAsync(SelectedMessages, Name, false),
            () => messageSaver.CanSaveMessages(cluster.Id));
        SaveSelectedAsFormattedCommand = new AsyncRelayCommand(
            () => messageSaver.SaveAsync(SelectedMessages, Name, true),
            () => messageSaver.CanSaveMessages(cluster.Id));
        SaveAllAsRawCommand = new AsyncRelayCommand(
            () => messageSaver.SaveAsync(CurrentMessages.Messages, Name, false),
            () => messageSaver.CanSaveMessages(cluster.Id));
        SaveAllAsFormattedCommand = new AsyncRelayCommand(
            () => messageSaver.SaveAsync(CurrentMessages.Messages, Name, true),
            () => messageSaver.CanSaveMessages(cluster.Id));

        CopyKeyCommand = new AsyncRelayCommand(CopyKeyAsync);
        CopyValueCommand = new AsyncRelayCommand(CopyValueAsync);

        Nodes.Add(this);
        IsSelected = true;
        IsExpanded = true;

        StartDate = DateTime.Now.Date;
        startTime = TimeOnly.FromDateTime(DateTime.Now);
        UpdateStartTimeText();

        FetchPositions = FetchPositionsForTopic;
        FetchPosition = FetchPositions[0];

        InitializeFormatters();

        IsActive = true;
        WeakReferenceMessenger.Default.Register<ConfigurationChangedMessage>(this, (r, m) =>
        {
            var config = settingsService.GetBrowserConfig();
            FontSize = config.FontSize;
            OnPropertyChanged(nameof(FetchCounts));
        });
    }

    private void InitializeFormatters()
    {
        var allFormatterNames = formatterService.GetAllFormatterNames();
        FormatterNames = allFormatterNames;
        ValueFormatterNames = formatterService.BuildFormatterNames(
            settingsService.GetValue(VALUE_FORMATTER_NAMES_SETTINGS_KEY), allFormatterNames);
        KeyFormatterNames = formatterService.BuildFormatterNames(
            settingsService.GetValue(KEY_FORMATTER_NAMES_SETTINGS_KEY),
            formatterService.GetBuiltInKeyFormatterNames());
    }

    private void UpdateStartTimeText()
    {
        var updated = startTime.ToString("HH:mm:ss");
        if (!updated.Equals(StartTimeText))
            StartTimeText = updated;
    }

    public void OnStartTimeTextLostFocus()
    {
        if (IsStartTimeValid)
        {
            UpdateStartTimeText();
        }
    }

    partial void OnFetchPositionChanged(string? value)
    {
        OnPropertyChanged(nameof(IsFetchBackwardEnabled));
        FetchBackward = value == "End";
    }

    private void OnClusterPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ClusterViewModel.StatusColor))
            OnPropertyChanged(nameof(StatusColor));
        else if (e.PropertyName == nameof(ClusterViewModel.Status))
        {
            OnPropertyChanged(nameof(IsChecking));
            if (cluster.Status == ConnectionState.Connected && Topics.Count == 0 && !isSyncingTopics)
                Dispatcher.UIThread.Post(() => _ = LoadTopicsAsync());
        }
        else if (e.PropertyName == nameof(ClusterViewModel.Name))
            Name = cluster.Name;
    }

    private static bool AreSameLogicalNode(ITreeNode? first, ITreeNode? second)
    {
        if (ReferenceEquals(first, second)) return true;
        if (first is null || second is null) return false;

        return (first, second) switch
        {
            (TopicViewModel a, TopicViewModel b) =>
                string.Equals(a.Name, b.Name, StringComparison.Ordinal),
            (PartitionViewModel a, PartitionViewModel b) =>
                a.Id == b.Id && string.Equals(a.TopicName, b.TopicName, StringComparison.Ordinal),
            _ => first.Type == second.Type && string.Equals(first.Name, second.Name, StringComparison.Ordinal)
        };
    }

    private ITreeNode? selectedNode;

    public ITreeNode? SelectedNode
    {
        get => field ?? selectedNode;
        set
        {
            var previousNode = selectedNode;
            if (value == null && selectedNode != null)
            {
                field = selectedNode;
                SetProperty(ref selectedNode, value);
                Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await Task.Delay(SELECTED_ITEM_DELAY_MS);
                    SetProperty(ref selectedNode, field);
                });
            }

            if (SetProperty(ref selectedNode, value))
            {
                SelectedNodeType = selectedNode?.Type ?? ITreeNode.NodeType.None;
                var logicalNodeChanged = !AreSameLogicalNode(previousNode, selectedNode);

                if (logicalNodeChanged)
                {
                    var newFetchPositions = SelectedNodeType == ITreeNode.NodeType.Partition
                        ? FetchPositionsForPartition
                        : FetchPositionsForTopic;
                    var previousFetchPosition = FetchPosition;

                    FetchPositions = newFetchPositions;
                    FetchPosition = previousFetchPosition != null && newFetchPositions.Contains(previousFetchPosition)
                        ? previousFetchPosition
                        : newFetchPositions[0];
                }

                if (selectedNode is { Type: ITreeNode.NodeType.Partition } or { Type: ITreeNode.NodeType.Topic })
                {
                    if (IsCurrent && logicalNodeChanged && !suppressFetchOnSelectionChange)
                        FetchMessages();
                }
            }
        }
    }

    private async Task CopyKeyAsync()
    {
        var key = CurrentMessages.CurrentMessage?.Key;
        if (key != null && SetClipboardText != null)
            await SetClipboardText(key);
    }

    private async Task CopyValueAsync()
    {
        var value = CurrentMessages.CurrentMessage?.DecodedMessage;
        if (value != null && SetClipboardText != null)
            await SetClipboardText(value);
    }
}
