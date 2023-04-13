using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using KafkaLens.Shared.Models;
using KafkaLens.Messages;
using KafkaLens.Shared;
using KafkaLens.Formatting;
using Serilog;
using Xunit;

namespace KafkaLens.ViewModels;

public partial class OpenedClusterViewModel: ViewModelBase, ITreeNode
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

    public ITreeNode.NodeType Type => ITreeNode.NodeType.CLUSTER;

    public bool IsSelected { get; set; }
    public ObservableCollection<ITreeNode> Children { get; }  = new();
    public bool IsExpanded { get; set; }

    [ObservableProperty] public List<IMessageFormatter> formatters;


    [ObservableProperty] public ICollection<string> formatterNames;

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

    public int[] FetchCounts => new int[] { 10, 25, 50, 100, 250, 500, 1000, 5000, 10000, 25000 };
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

    public string StartTimeText
    {
        get => StartTime.ToShortDateString() + " " + StartTime.ToLongTimeString();
        set
        {
            if (SetProperty(ref startTimeText, value))
            {
                if (DateTime.TryParse(startTimeText, out DateTime time))
                {
                    StartTime = time;
                }
            }
        }
    }

    [ObservableProperty]
    public DateTime startTime;

    private int fontSize = 14;
    public int FontSize
    {
        get => fontSize;
        set => SetProperty(ref fontSize, value, true);
    }

    [ObservableProperty]
    private string? fetchPosition = null;

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

        formatters = FormatterFactory.GetFormatters();
        FormatterNames = formatters.ConvertAll(f => f.Name);
        DefaultFormatter = Formatters.FirstOrDefault();

        IsActive = true;
    }

    private void Close()
    {
        _ = Messenger.Send(new CloseTabMessage(this));
    }

    private async Task UpdateFormatterAsync()
    {
        //((IMessageSource)SelectedNode).FormatterName = "Json";
        Console.WriteLine("");
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
            var viewModel = new TopicViewModel(KafkaLensClient, topic, null);
            Topics.Add(viewModel);
            Children.Add(viewModel);
        }
    }

    private IMessageFormatter DefaultFormatter { get; set; }

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
    public static FormatterFactory FormatterFactory { get; set; }

    MessageStream? messages = null;
    private List<IMessageLoadListener> messageLoadListeners = new();
    private DateTime startDate;
    private string startTimeText;

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
            TopicViewModel topic => KafkaLensClient.GetMessageStream(clusterViewModel.Id, topic.Name,
                fetchOptions),

            PartitionViewModel partition => KafkaLensClient.GetMessageStream(clusterViewModel.Id,
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
                pendingMessages.Add(viewModel);
            }
            Dispatcher.UIThread.InvokeAsync(UpdateMessages);
            Log.Debug("Pending messages = {Count}", pendingMessages.Count);
        }
    }

    private IMessageFormatter? GuessFormatter(Message message) {
        IMessageFormatter best = null;
        int maxLength = 0;
        foreach (var formatter in Formatters) {
            var text = formatter.Format(message.Value, true);
            if (text == null) continue;
            if (text.Length > maxLength) {
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
                var timeStamp = StartTime;
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