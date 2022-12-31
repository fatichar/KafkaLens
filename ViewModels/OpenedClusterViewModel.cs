using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using KafkaLens.Formatting;
using KafkaLens.Shared.Models;
using KafkaLens.Messages;
using KafkaLens.Shared;
using Serilog;

namespace KafkaLens.ViewModels;

public sealed class OpenedClusterViewModel : ObservableRecipient, ITreeNode
{
    private readonly ISettingsService settingsService;
    private readonly ClusterViewModel clusterViewModel;
    private IKafkaLensClient KafkaLensClient => clusterViewModel.KafkaLensClient;
    private const int DEFAULT_FETCH_COUNT = 10;
    public static IList<string> FetchPositionsForTopic { get; } = new List<string>();
    public static IList<string> FetchPositionsForPartition { get; } = new List<string>();
    public IList<string> fetchPositions = new List<string>();
    public IList<string> FetchPositions
    {
        get => fetchPositions;
        set => SetProperty(ref fetchPositions, value);
    }

    private static IDictionary<string, IMessageFormatter?> formatters = new Dictionary<string, IMessageFormatter?>();

    private static IMessageFormatter jsonFormatter = new JsonFormatter();
    private static IMessageFormatter textFormatter = new TextFormatter();

    public ITreeNode.NodeType Type => ITreeNode.NodeType.CLUSTER;

    public bool IsSelected { get; set; }
    public bool IsExpanded { get; set; }

    public ICollection<string> MessageFormats => formatters.Keys;

    public RelayCommand FetchMessagesCommand { get; }
    public IAsyncRelayCommand ChangeFormatterCommand { get; }

    public string Name { get; }
    public string Address => clusterViewModel.Address;

    public ObservableCollection<ITreeNode> Nodes { get; } = new();
    public ObservableCollection<TopicViewModel> Topics { get; } = new();

    public MessagesViewModel CurrentMessages { get; } = new();
    private readonly List<MessageViewModel> pendingMessages = new();

    private ITreeNode? selectedNode;

    private ITreeNode.NodeType selectedNodeType = ITreeNode.NodeType.NONE;
    public ITreeNode.NodeType SelectedNodeType
    {
        get => selectedNodeType;
        set => SetProperty(ref selectedNodeType, value);
    }

    public int[] FetchCounts => new int[] { 10, 25, 50, 100, 250, 500, 1000, 5000 };
    public int FetchCount { get; set; } = 10;
    public string? StartOffset { get; }
    public DateTime StartDate
    {
        get => startDate;
        set
        {
            startDate = value;
        }
    }
    public DateTime StartTime { get; set; }

    private int fontSize = 14;
    public int FontSize
    {
        get => fontSize;
        set => SetProperty(ref fontSize, value, true);
    }

    private string? fetchPosition = null;
    public string? FetchPosition
    {
        get => fetchPosition;
        set => SetProperty(ref fetchPosition, value);
    }

    static OpenedClusterViewModel()
    {
        formatters.Add(textFormatter.Name, textFormatter);
        formatters.Add(jsonFormatter.Name, jsonFormatter);

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
        ClusterViewModel clusterViewModel,
        string name)
    {
        this.settingsService = settingsService;
        this.clusterViewModel = clusterViewModel;
        Name = name;

        CloseTabCommand = new RelayCommand(Close);
        FetchMessagesCommand = new RelayCommand(FetchMessages);
        ChangeFormatterCommand = new AsyncRelayCommand(UpdateFormatterAsync);

        Nodes.Add(this);
        IsSelected = true;
        IsExpanded = true;

        StartDate = DateTime.Today;
        StartTime = DateTime.Now;

        FetchPositions = FetchPositionsForTopic;
        FetchPosition = FetchPositions[0];

        IsActive = true;
    }

    private void Close()
    {
        _ = Messenger.Send(new CloseTabMessage(this));
    }

    private async Task UpdateFormatterAsync()
    {
        // SelectedNode.Formatter =
    }

    internal async Task LoadTopicsAsync()
    {
        if (clusterViewModel.Topics.Count == 0)
        {
            await clusterViewModel.LoadTopicsCommand.ExecuteAsync(null);
        }
        Topics.Clear();
        foreach (var topic in clusterViewModel.Topics)
        {
            Topics.Add(new TopicViewModel(KafkaLensClient, topic, jsonFormatter));
        }
    }

    public ITreeNode? SelectedNode
    {
        get => selectedNode;
        set
        {
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
                    FetchMessagesCommand.Execute(null);
                }
            }
        }
    }

    public string ClusterId => clusterViewModel.Id;
    public IRelayCommand CloseTabCommand { get; }

    MessageStream? messages = null;
    private List<IMessageLoadListener> messageLoadListeners = new();
    private DateTime startDate;

    private void FetchMessages()
    {
        if (selectedNode == null)
        {
            return;
        }

        var fetchOptions = CreateFetchOptions();
        messageLoadListeners.ForEach(listener => listener.MessageLoadingStarted());

        messages = selectedNode switch
        {
            TopicViewModel topic => KafkaLensClient.GetMessageStream(clusterViewModel.Id, topic.Name,
                fetchOptions),

            PartitionViewModel partition => KafkaLensClient.GetMessageStream(clusterViewModel.Id,
                partition.TopicName, partition.Id, fetchOptions),

            _ => null
        };

        if (messages != null)
        {
            CurrentMessages.Clear();
            messages.Messages.CollectionChanged += OnMessagesChanged;
        }
    }

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var node = (IMessageSource?)SelectedNode;
        var formatter = node?.Formatter ?? jsonFormatter;
        lock (pendingMessages)
        {
            Log.Debug("Pending messages = {Count}", pendingMessages.Count);
            Log.Debug("Received {Count} messages", e.NewItems?.Count);
            foreach (var msg in e.NewItems ?? new List<Message>())
            {
                MessageViewModel viewModel = new((Message)msg, formatter);
                pendingMessages.Add(viewModel);
            }
            Log.Debug("Pending messages = {Count}", pendingMessages.Count);
        }
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
                var timeStamp = StartDate + StartTime.TimeOfDay;
                var epochMs = (long)(timeStamp.ToUniversalTime() - new DateTime(1970, 1, 1)).TotalMilliseconds;
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