using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Text;
using System.Threading;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KafkaLens.Shared.Models;
using KafkaLens.Shared;
using KafkaLens.Formatting;
using Serilog;
using Xunit;

namespace KafkaLens.ViewModels;

public partial class OpenedClusterViewModel : ViewModelBase, ITreeNode
{
    const int SELECTED_ITEM_DELAY_MS = 3;

    private readonly ITopicSettingsService topicSettingsService;
    private readonly ClusterViewModel cluster;
    private IKafkaLensClient KafkaLensClient => cluster.Client;
    private static IList<string> FetchPositionsForTopic { get; } = new List<string>();
    private static IList<string> FetchPositionsForPartition { get; } = new List<string>();

    public IList<string> FetchPositions
    {
        get;
        set => SetProperty(ref field, value);
    }

    public ITreeNode.NodeType Type => ITreeNode.NodeType.CLUSTER;

    [ObservableProperty] private bool isSelected;
    [ObservableProperty] private bool isExpanded;
    public ObservableCollection<ITreeNode> Children { get; } = new();

    [ObservableProperty] private List<IMessageFormatter> formatters;

    [ObservableProperty] private IList<string> formatterNames;
    [ObservableProperty] private IList<string> valueFormatterNames;
    [ObservableProperty] private IList<string> keyFormatterNames;

    public RelayCommand ToggleFetchCommand { get; }
    public RelayCommand RefreshCommand { get; }
    public IAsyncRelayCommand ChangeFormatterCommand { get; }
    public IAsyncRelayCommand SaveTopicSettingsCommand { get; }
    public AsyncRelayCommand SaveSelectedAsRawCommand { get; set; }
    public AsyncRelayCommand SaveSelectedAsFormattedCommand { get; set; }
    public AsyncRelayCommand SaveAllAsRawCommand { get; set; }
    public AsyncRelayCommand SaveAllAsFormattedCommand { get; set; }

    [ObservableProperty] private string name;

    public string Address => cluster.Address;

    public string StatusColor => cluster.StatusColor;

    public ObservableCollection<ITreeNode> Nodes { get; } = new();
    public ObservableCollection<TopicViewModel> Topics { get; } = new();

    public MessagesViewModel CurrentMessages { get; } = new();
    private readonly List<MessageViewModel> pendingMessages = new();

    public ITreeNode.NodeType SelectedNodeType
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                OnPropertyChanged(nameof(IsFetchOptionsEnabled));
            }
        }
    } = ITreeNode.NodeType.NONE;

    public bool IsFetchOptionsEnabled => SelectedNodeType == ITreeNode.NodeType.TOPIC ||
                                         SelectedNodeType == ITreeNode.NodeType.PARTITION;

    public int[] FetchCounts => new int[] { 10, 25, 50, 100, 250, 500, 1000, 5000, 10000, 25000 };
    public int FetchCount { get; set; } = 10;
    [ObservableProperty] private string? startOffset;

    private TimeOnly startTime;
    [ObservableProperty] private bool isStartTimeValid = true;

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
                    UpdateStartTimeText();
                }
            }
        }
    }

    [ObservableProperty] private DateTime startDate;

    private DateTime StartDateTime => StartDate.Date + startTime.ToTimeSpan();

    public int FontSize
    {
        get;
        set => SetProperty(ref field, value, true);
    } = 14;

    [ObservableProperty] private bool isLoading;

    [ObservableProperty] private string? fetchPosition;

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
        ClusterViewModel cluster,
        string name)
    {
        this.topicSettingsService = topicSettingsService;
        this.cluster = cluster;
        this.cluster.PropertyChanged += OnClusterPropertyChanged;
        Name = name;

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
        ChangeFormatterCommand = new AsyncRelayCommand(UpdateFormatterAsync);
        SaveTopicSettingsCommand = new AsyncRelayCommand(SaveTopicSettingsAsync);

        SaveSelectedAsRawCommand = new AsyncRelayCommand(SaveSelectedMessagesAsRaw, CanSaveMessages);
        SaveSelectedAsFormattedCommand = new AsyncRelayCommand(SaveSelectedMessagesAsFormatted, CanSaveMessages);
        SaveAllAsRawCommand = new AsyncRelayCommand(SaveAllMessagesAsRaw, CanSaveMessages);
        SaveAllAsFormattedCommand = new AsyncRelayCommand(SaveAllMessagesAsFormatted, CanSaveMessages);

        Nodes.Add(this);
        IsSelected = true;
        IsExpanded = true;

        StartDate = DateTime.Now.Date;
        startTime = TimeOnly.FromDateTime(DateTime.Now);
        UpdateStartTimeText();

        FetchPositions = FetchPositionsForTopic;
        FetchPosition = FetchPositions[0];

        formatters = FormatterFactory.GetFormatters();
        FormatterNames = formatters.ConvertAll(f => f.Name);

        var names = new List<string>(FormatterNames);
        names.Insert(0, "Auto");
        ValueFormatterNames = names;

        DefaultFormatter = Formatters.FirstOrDefault() ?? new TextFormatter();

        KeyFormatterNames = new List<string> { "Auto", "Text", "Number" };

        IsActive = true;
    }

    private void UpdateStartTimeText()
    {
        var updated = startTime.ToString("HH:mm:ss");
        if (!updated.Equals(StartTimeText))
        {
            StartTimeText = updated;
        }
    }

    private void OnClusterPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ClusterViewModel.StatusColor))
        {
            OnPropertyChanged(nameof(StatusColor));
        }
        else if (e.PropertyName == nameof(ClusterViewModel.IsConnected))
        {
            if (cluster.IsConnected == true && Topics.Count == 0)
            {
                _ = LoadTopicsAsync();
            }
        }
        else if (e.PropertyName == nameof(ClusterViewModel.Name))
        {
            Name = cluster.Name;
        }
    }

    #region SAVE MESSAGES

    private static readonly string SAVE_MESSAGES_DIR = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "KafkaLens",
        "SavedMessages");

    private bool CanSaveMessages()
    {
        return cluster.Client.CanSaveMessages;
    }

    private async Task SaveAllMessagesAsRaw()
    {
        await SaveAsync(CurrentMessages.Messages, false);
    }

    private async Task SaveAllMessagesAsFormatted()
    {
        await SaveAsync(CurrentMessages.Messages, true);
    }

    private async Task SaveSelectedMessagesAsRaw()
    {
        await SaveAsync(SelectedMessages, false);
    }

    private async Task SaveSelectedMessagesAsFormatted()
    {
        await SaveAsync(SelectedMessages, true);
    }

    private async Task SaveAsync(IList<MessageViewModel> messages, bool formatted)
    {
        if (messages.Count == 0)
            return;

        await Task.Run(() => SaveAllInternal(messages, formatted));
    }

    private void SaveAllInternal(IList<MessageViewModel> messages, bool formatted)
    {
        Log.Information($"Saving {messages.Count} messages");

        // Pre-create partition directories
        var baseDir = Path.Join(SAVE_MESSAGES_DIR, Name);

        var partitionDirs = messages
            .Select(m => Path.Join(baseDir, m.Topic, m.Partition.ToString()))
            .Distinct();

        foreach (var dir in partitionDirs)
            Directory.CreateDirectory(dir);

        var throttler = new SemaphoreSlim(8); // tune 4–12

        var tasks = messages.Select(async msg =>
        {
            var dir = Path.Join(baseDir, msg.Topic, msg.Partition.ToString());

            await throttler.WaitAsync().ConfigureAwait(false);
            try
            {
                await SaveSingleAsync(dir, msg, formatted)
                    .ConfigureAwait(false);
            }
            finally
            {
                throttler.Release();
            }
        });

        Task.WhenAll(tasks)
            .ContinueWith(t => { Log.Information($"Saved {messages.Count} messages"); })
            .ConfigureAwait(false);
    }


    private async Task SaveSingleAsync(
        string dir,
        MessageViewModel msg,
        bool formatted)
    {
        var filePath = Path.Join(dir, msg.Offset + GetExtension(formatted));

        if (!formatted)
        {
            await SaveRaw(msg, filePath);
        }
        else
        {
            await SaveFormatted(msg, filePath);
        }
    }

    private static async Task SaveFormatted(MessageViewModel msg, string filePath)
    {
        msg.PrettyFormat();

        var sb = new StringBuilder(512);

        sb.AppendLine($"Key: {msg.Key}");
        sb.AppendLine($"Timestamp: {msg.Timestamp}");
        sb.AppendLine($"Partition: {msg.Partition}");
        sb.AppendLine($"Offset: {msg.Offset}");

        if (msg.Message.Headers.Count > 0)
        {
            sb.AppendLine("Headers:");
            foreach (var header in msg.Message.Headers)
            {
                sb.Append("  ")
                    .Append(header.Key)
                    .Append(": ")
                    .AppendLine(Encoding.UTF8.GetString(header.Value));
            }
        }

        sb.AppendLine();
        sb.AppendLine(msg.DisplayText);

        await File.WriteAllTextAsync(filePath, sb.ToString())
            .ConfigureAwait(false);
    }

    private static async Task SaveRaw(MessageViewModel msg, string filePath)
    {
        await using var fileStream = new FileStream(
            filePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,   // larger buffer for binary
            useAsync: true);

        msg.Message.Serialize(fileStream);
        await fileStream.FlushAsync().ConfigureAwait(false);
    }


    private static string GetExtension(bool formatted)
    {
        return formatted ? ".txt" : ".klm";
    }

    #endregion

    private async Task UpdateFormatterAsync()
    {
        //((IMessageSource)SelectedNode).FormatterName = "Json";
        Console.WriteLine("");
    }

    internal async Task LoadTopicsAsync()
    {
        try
        {
            await cluster.LoadTopicsCommand.ExecuteAsync(null);
            Topics.Clear();
            foreach (var topic in cluster.Topics)
            {
                var settings = topicSettingsService.GetSettings(cluster.Id, topic.Name);
                var valueFormatter = NormalizeFormatterName(settings.ValueFormatter, ValueFormatterNames);
                var keyFormatter = NormalizeFormatterName(settings.KeyFormatter, KeyFormatterNames);
                var viewModel = new TopicViewModel(topic, valueFormatter, keyFormatter);
                Topics.Add(viewModel);
            }

            FilterTopics();
        }
        catch (Exception e)
        {
            Serilog.Log.Error(e, "Failed to load topics for opened cluster {ClusterName}", Name);
        }
    }

    [ObservableProperty] private string filterText = "";

    partial void OnFilterTextChanged(string value)
    {
        FilterTopics();
    }

    internal void FilterTopics()
    {
        Children.Clear();
        foreach (var topic in Topics)
        {
            if (string.IsNullOrWhiteSpace(FilterText) ||
                topic.Name.Contains(FilterText, StringComparison.OrdinalIgnoreCase))
            {
                Children.Add(topic);
            }
        }
    }

    private IMessageFormatter DefaultFormatter { get; set; }

    private ITreeNode? selectedNode;

    public ITreeNode? SelectedNode
    {
        get => field ?? selectedNode;
        set
        {
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
                SelectedNodeType = selectedNode?.Type ?? ITreeNode.NodeType.NONE;

                FetchPositions = SelectedNodeType == ITreeNode.NodeType.PARTITION
                    ? FetchPositionsForPartition
                    : FetchPositionsForTopic;
                FetchPosition = null;
                FetchPosition = FetchPositions[0];
                if (selectedNode is { Type: ITreeNode.NodeType.PARTITION } or { Type: ITreeNode.NodeType.TOPIC })
                {
                    if (IsCurrent)
                    {
                        FetchMessages();
                    }
                }
            }
        }
    }

    public string ClusterId => cluster.Id;
    public static FormatterFactory FormatterFactory { get; set; } = null!;
    public IList<MessageViewModel> SelectedMessages { get; set; } = new List<MessageViewModel>();
    public bool IsCurrent { get; set; }

    MessageStream? messages = null;
    private readonly List<IMessageLoadListener> messageLoadListeners = new();
    private CancellationTokenSource? fetchCts;

    private void OnStreamFinished()
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            IsLoading = false;
            messageLoadListeners.ForEach(listener => listener.MessageLoadingFinished());
        });
    }

    private void StopLoading()
    {
        fetchCts?.Cancel();
        IsLoading = false;
    }

    internal void FetchMessages()
    {
        if (selectedNode == null)
        {
            return;
        }

        fetchCts?.Cancel();
        fetchCts = new CancellationTokenSource();

        if (messages != null)
        {
            messages.Messages.CollectionChanged -= OnMessagesChanged;
            messages.Finished -= OnStreamFinished;
        }

        CurrentMessages.Clear();
        IsLoading = true;

        var fetchOptions = CreateFetchOptions();
        messageLoadListeners.ForEach(listener => listener.MessageLoadingStarted());

        messages = selectedNode switch
        {
            TopicViewModel topic => KafkaLensClient.GetMessageStream(cluster.Id, topic.Name,
                fetchOptions, fetchCts.Token),

            PartitionViewModel partition => KafkaLensClient.GetMessageStream(cluster.Id,
                partition.TopicName, partition.Id, fetchOptions, fetchCts.Token),

            _ => null
        };

        if (messages != null)
        {
            messages.Messages.CollectionChanged += OnMessagesChanged;
            messages.Finished += OnStreamFinished;
        }
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var node = (IMessageSource?)SelectedNode;
        if (node == null)
        {
            return;
        }

        bool settingsChanged = false;
        var topicName = GetCurrentTopicName();

        if (node.FormatterName == null || node.FormatterName == "Auto")
        {
            Assert.True(e.NewItems?.Count > 0);
            var message = (Message)e.NewItems![0]!;
            var formatter = GuessValueFormatter(message);
            node.FormatterName = formatter?.Name ?? DefaultFormatter.Name;
            settingsChanged = true;
            Log.Information("Guessed value formatter {Formatter} for topic {Topic}", node.FormatterName, topicName);
        }

        if (node.KeyFormatterName == null || node.KeyFormatterName == "Auto")
        {
            Assert.True(e.NewItems?.Count > 0);
            var message = (Message)e.NewItems![0]!;
            var formatter = GuessKeyFormatter(message);
            node.KeyFormatterName = formatter.Name;
            settingsChanged = true;
            Log.Information("Guessed key formatter {Formatter} for topic {Topic}", node.KeyFormatterName, topicName);
        }

        if (settingsChanged)
        {
            topicSettingsService.SetSettings(cluster.Id, topicName, new TopicSettings
            {
                KeyFormatter = node.KeyFormatterName,
                ValueFormatter = node.FormatterName
            });
        }

        lock (pendingMessages)
        {
            Log.Debug("Pending messages = {Count}", pendingMessages.Count);
            Log.Debug("Received {Count} messages", e.NewItems?.Count);
            foreach (var msg in e.NewItems ?? new List<Message>())
            {
                var viewModel = new MessageViewModel((Message)msg, node.FormatterName, node.KeyFormatterName);
                viewModel.Topic = topicName;
                pendingMessages.Add(viewModel);
            }

            Dispatcher.UIThread.InvokeAsync(UpdateMessages);
            Log.Debug("Pending messages = {Count}", pendingMessages.Count);
        }
    }

    private string GetCurrentTopicName()
    {
        switch (selectedNode)
        {
            case TopicViewModel topic:
                return topic.Name;
            case PartitionViewModel partition:
                return partition.TopicName;
            default:
                throw new InvalidOperationException();
        }
    }

    private IMessageFormatter? GuessValueFormatter(Message message)
    {
        IMessageFormatter? best = null;
        int maxLength = 0;

        // Disable console output, as some formatters may write to it.
        var originalOut = Console.Out;
        Console.SetOut(TextWriter.Null);
        try
        {
            foreach (IMessageFormatter formatter in Formatters)
            {
                try
                {
                    var text = formatter.Format(message.Value ?? Array.Empty<byte>(), true);
                    if (text == null) continue;
                    if (text.Length > maxLength)
                    {
                        maxLength = text.Length;
                        best = formatter;
                    }
                }
                catch
                {
                    // Formatter doesn't support this message type — skip silently
                }
            }
        }
        finally
        {
            // Restore console output.
            Console.SetOut(originalOut);
        }

        return best;
    }

    private IMessageFormatter GuessKeyFormatter(Message message)
    {
        // Try NumberFormatter first
        var numberFormatter = FormatterFactory.Instance.GetFormatter("Number");
        if (numberFormatter.Format(message.Key ?? Array.Empty<byte>(), false) != null)
        {
            return numberFormatter;
        }

        return FormatterFactory.Instance.GetFormatter("Text");
    }

    internal static string NormalizeFormatterName(string? formatterName, IList<string> allowedNames)
    {
        if (string.IsNullOrWhiteSpace(formatterName) || formatterName == "Auto")
        {
            return "Auto";
        }

        return allowedNames.Contains(formatterName)
            ? formatterName
            : "Auto";
    }

    internal static bool CanApplyFormatterToLoadedMessages(string? formatterName, IList<string> allowedNames)
    {
        return !string.IsNullOrWhiteSpace(formatterName) &&
               formatterName != "Auto" &&
               allowedNames.Contains(formatterName);
    }

    [ObservableProperty] private bool applyToAllClusters;

    internal Task SaveTopicSettingsAsync()
    {
        if (SelectedNode is not IMessageSource node) return Task.CompletedTask;

        var topicName = GetCurrentTopicName();
        var settings = new TopicSettings
        {
            KeyFormatter = node.KeyFormatterName ?? "Auto",
            ValueFormatter = node.FormatterName ?? "Auto"
        };
        topicSettingsService.SetSettings(cluster.Id, topicName, settings, ApplyToAllClusters);

        // Re-format existing messages
        foreach (var msg in CurrentMessages.Messages)
        {
            if (CanApplyFormatterToLoadedMessages(settings.ValueFormatter, ValueFormatterNames))
            {
                msg.FormatterName = settings.ValueFormatter;
            }

            if (CanApplyFormatterToLoadedMessages(settings.KeyFormatter, KeyFormatterNames))
            {
                msg.KeyFormatterName = settings.KeyFormatter;
            }
        }

        return Task.CompletedTask;
    }

    public void UpdateMessages()
    {
        lock (pendingMessages)
        {
            Log.Debug("UI: Pending messages = {Count}", pendingMessages.Count);
            if (pendingMessages.Count > 0)
            {
                CurrentMessages.AddRange(pendingMessages);
                Log.Debug("UI: Loaded {Count} messages", pendingMessages.Count);
                pendingMessages.Clear();
            }
        }

        if (!messages?.HasMore ?? false)
        {
            Log.Debug("UI: No more messages");
            IsLoading = false;
        }
    }

    internal FetchOptions CreateFetchOptions()
    {
        FetchPosition start;
        FetchPosition? end = null;
        switch (FetchPosition)
        {
            case "End":
                end = Shared.Models.FetchPosition.END;
                start = new(PositionType.OFFSET, Shared.Models.FetchPosition.END.Offset - FetchCount);
                break;
            case "Start":
                start = Shared.Models.FetchPosition.START;
                break;
            case "Timestamp":
                var epochMs = (long)(StartDateTime.ToUniversalTime() - new DateTime(1970, 1, 1)).TotalMilliseconds;
                start = new(PositionType.TIMESTAMP, epochMs);
                break;
            case "Offset":
                start = new(PositionType.OFFSET, long.TryParse(StartOffset, out var offset) ? offset : -1);
                break;
            default:
                throw new Exception("Invalid fetch position " + FetchPosition);
        }

        var fetchOptions = new FetchOptions(start, end);
        fetchOptions.Limit = FetchCount;
        return fetchOptions;
    }
}