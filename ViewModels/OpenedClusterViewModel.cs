using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
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

    private readonly ISettingsService settingsService;
    private readonly ClusterViewModel cluster;
    private IKafkaLensClient KafkaLensClient => cluster.Client;
    public static IList<string> FetchPositionsForTopic { get; } = new List<string>();
    public static IList<string> FetchPositionsForPartition { get; } = new List<string>();
    public IList<string> fetchPositions = new List<string>();

    public IList<string> FetchPositions
    {
        get => fetchPositions;
        set => SetProperty(ref fetchPositions, value);
    }

    public ITreeNode.NodeType Type => ITreeNode.NodeType.CLUSTER;

    [ObservableProperty] private bool isSelected;
    [ObservableProperty] private bool isExpanded;
    public ObservableCollection<ITreeNode> Children { get; } = new();

    [ObservableProperty] public List<IMessageFormatter> formatters;

    [ObservableProperty] public ICollection<string> formatterNames;

    public RelayCommand FetchMessagesCommand { get; }
    public IAsyncRelayCommand ChangeFormatterCommand { get; }
    public AsyncRelayCommand SaveSelectedAsRawCommand { get; set; }
    public AsyncRelayCommand SaveSelectedAsFormattedCommand { get; set; }
    public AsyncRelayCommand SaveAllAsRawCommand { get; set; }
    public AsyncRelayCommand SaveAllAsFormattedCommand { get; set; }

    [ObservableProperty]
    private string name;

    public string Address => cluster.Address;

    public ObservableCollection<ITreeNode> Nodes { get; } = new();
    public ObservableCollection<TopicViewModel> Topics { get; } = new();

    public MessagesViewModel CurrentMessages { get; } = new();
    private readonly List<MessageViewModel> pendingMessages = new();

    public ITreeNode.NodeType SelectedNodeType
    {
        get;
        set => SetProperty(ref field, value);
    } = ITreeNode.NodeType.NONE;

    public int[] FetchCounts => new int[] { 10, 25, 50, 100, 250, 500, 1000, 5000, 10000, 25000 };
    public int FetchCount { get; set; } = 10;
    public string? StartOffset { get; }

    private TimeOnly startTime;
    [ObservableProperty]
    private bool isStartTimeValid = true;

    public string StartTimeText
    {
        get;
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

    [ObservableProperty]
    private DateTime startDate;

    private DateTime StartDateTime => StartDate.Date + startTime.ToTimeSpan();

    public int FontSize
    {
        get;
        set => SetProperty(ref field, value, true);
    } = 14;

    [ObservableProperty] private string? fetchPosition = null;

    static OpenedClusterViewModel()
    {
        FetchPositionsForTopic.Add("End");
        FetchPositionsForTopic.Add("Timestamp");
        //FetchPositionsForTopic.Add("Start");

        FetchPositionsForPartition.Add("End");
        FetchPositionsForPartition.Add("Timestamp");
        //FetchPositionsForPartition.Add("Offset");
        //FetchPositionsForPartition.Add("Start");
    }

    public OpenedClusterViewModel(
        ISettingsService settingsService,
        ClusterViewModel cluster,
        string name)
    {
        this.settingsService = settingsService;
        this.cluster = cluster;
        Name = name;

        FetchMessagesCommand = new RelayCommand(FetchMessages);
        ChangeFormatterCommand = new AsyncRelayCommand(UpdateFormatterAsync);

        SaveSelectedAsRawCommand = new AsyncRelayCommand(SaveSelectedMessagesAsRaw);
        SaveSelectedAsFormattedCommand = new AsyncRelayCommand(SaveSelectedMessagesAsFormatted);
        SaveAllAsRawCommand = new AsyncRelayCommand(SaveAllMessagesAsRaw);
        SaveAllAsFormattedCommand = new AsyncRelayCommand(SaveAllMessagesAsFormatted);

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
        DefaultFormatter = Formatters.FirstOrDefault() ?? new TextFormatter();

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

    #region SAVE MESSAGES

    private const string SAVE_MESSAGES_DIR = "saved_messages";

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
        {
            return;
        }

        foreach (var msg in messages)
        {
            await SaveAsync(msg, formatted);
        }
    }

    private async Task SaveAsync(MessageViewModel msg, bool formatted)
    {
        var dir = Path.Join(SAVE_MESSAGES_DIR, Name, msg.Topic, msg.Partition.ToString());
        Directory.CreateDirectory(dir);

        var fileName = msg.Offset + GetExtension(formatted);
        var filePath = Path.Join(dir, fileName);
        // save as binary
        if (!formatted)
        {
            await File.WriteAllBytesAsync(filePath, msg.message.Value);
        }
        else
        {
            // save as formatted
            msg.PrettyFormat();
            await File.WriteAllTextAsync(filePath, msg.DisplayText);
        }
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
        await cluster.LoadTopicsCommand.ExecuteAsync(null);
        Topics.Clear();
        foreach (var topic in cluster.Topics)
        {
            var viewModel = new TopicViewModel(topic, null);
            Topics.Add(viewModel);
            Children.Add(viewModel);
        }
    }

    private IMessageFormatter DefaultFormatter { get; set; }

    private ITreeNode? selectedNode;

    public ITreeNode? SelectedNode
    {
        get => selectedNode;
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

                // FetchPositions = SelectedNodeType == ITreeNode.NodeType.PARTITION
                //     ? FetchPositionsForPartition
                //     : FetchPositionsForTopic;
                // FetchPosition = null;
                // FetchPosition = FetchPositions[0];
                if (selectedNode is { Type: ITreeNode.NodeType.PARTITION } or { Type: ITreeNode.NodeType.TOPIC })
                {
                    if (IsCurrent)
                    {
                        FetchMessagesCommand.Execute(null);
                    }
                }
            }
        }
    }

    public string ClusterId => cluster.Id;
    public static FormatterFactory FormatterFactory { get; set; }
    public IList<MessageViewModel> SelectedMessages { get; set; }
    public bool IsCurrent { get; set; }

    MessageStream? messages = null;
    private readonly List<IMessageLoadListener> messageLoadListeners = new();

    private void FetchMessages()
    {
        if (selectedNode == null)
        {
            return;
        }

        if (messages != null)
        {
            messages.Messages.CollectionChanged -= OnMessagesChanged;
        }

        CurrentMessages.Clear();

        var fetchOptions = CreateFetchOptions();
        messageLoadListeners.ForEach(listener => listener.MessageLoadingStarted());

        messages = selectedNode switch
        {
            TopicViewModel topic => KafkaLensClient.GetMessageStream(cluster.Id, topic.Name,
                fetchOptions),

            PartitionViewModel partition => KafkaLensClient.GetMessageStream(cluster.Id,
                partition.TopicName, partition.Id, fetchOptions),

            _ => null
        };

        if (messages != null)
        {
            messages.Messages.CollectionChanged += OnMessagesChanged;
        }
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var node = (IMessageSource?)SelectedNode;
        if (node == null)
        {
            return;
        }

        if (node.FormatterName == null)
        {
            Assert.True(e.NewItems?.Count > 0);
            var message = (Message)e.NewItems[0];
            var formatter = GuessFormatter(message);
            node.FormatterName = formatter?.Name ?? DefaultFormatter.Name;
        }

        //var formatter = node?.FormatterName ?? DefaultFormatter.Name;
        lock (pendingMessages)
        {
            Log.Debug("Pending messages = {Count}", pendingMessages.Count);
            Log.Debug("Received {Count} messages", e.NewItems?.Count);
            foreach (var msg in e.NewItems ?? new List<Message>())
            {
                var viewModel = new MessageViewModel((Message)msg, node.FormatterName);
                viewModel.Topic = GetCurrentTopicName();
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

    private IMessageFormatter? GuessFormatter(Message message)
    {
        IMessageFormatter? best = null;
        int maxLength = 0;
        foreach (IMessageFormatter formatter in Formatters)
        {
            var text = formatter.Format(message.Value, true);
            if (text == null) continue;
            if (text.Length > maxLength)
            {
                maxLength = text.Length;
                best = formatter;
            }
        }

        return best;
    }

    private void OnMessagesFinished()
    {
        messageLoadListeners.ForEach(listener => listener.MessageLoadingFinished());
    }

    public void AddMessageLoadListener(IMessageLoadListener listener)
    {
        messageLoadListeners.Add(listener);
    }

    public void RemoveMessageLoadListener(IMessageLoadListener listener)
    {
        messageLoadListeners.Remove(listener);
    }

    public void UpdateMessages()
    {
        lock (pendingMessages)
        {
            Log.Debug("UI: Pending messages = {Count}", pendingMessages.Count);
            if (pendingMessages.Count > 0)
            {
                pendingMessages.ForEach(CurrentMessages.Add);
                Log.Information("UI: Loaded {Count} messages", pendingMessages.Count);
                pendingMessages.Clear();
            }
        }

        if (!messages?.HasMore ?? false)
        {
            Log.Information("UI: No more messages");
            OnMessagesFinished();
        }
    }

    private FetchOptions CreateFetchOptions()
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