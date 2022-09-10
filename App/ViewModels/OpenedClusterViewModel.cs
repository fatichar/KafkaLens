﻿using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KafkaLens.App.Formating;
using KafkaLens.Core.Services;
using KafkaLens.Shared.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Threading.Tasks;
using System.Windows.Threading;
using Serilog;

namespace KafkaLens.App.ViewModels
{
    public sealed class OpenedClusterViewModel : ObservableRecipient, ITreeNode
    {
        private readonly ISettingsService settingsService;
        private readonly IClusterService clusterService;
        private readonly ClusterViewModel clusterViewModel;
        private const int DEFAULT_FETCH_COUNT = 10;
        public static IList<string> FetchPositionsForTopic { get; } = new List<string>();
        public static IList<string> FetchPositionsForPartition { get; } = new List<string>();
        public IList<string> fetchPositions;
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
        public bool IsExpandable => true;
        public bool IsExpanded
        {
            get;
            set;
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

        public ICollection<string> MessageFormats => formatters.Keys;

        public RelayCommand FetchMessagesCommand { get; }
        public IAsyncRelayCommand ChangeFormatterCommand { get; }

        public string Name { get; }
        public string Address => clusterViewModel.Address;

        public ObservableCollection<ITreeNode> Nodes { get; } = new();
        public ObservableCollection<TopicViewModel> Topics { get; } = new();

        public MessagesViewModel CurrentMessages { get; } = new();
        private List<MessageViewModel> pendingMessages = new();

        private ITreeNode? selectedNode;

        private ITreeNode.NodeType selectedNodeType = ITreeNode.NodeType.NONE;
        public ITreeNode.NodeType SelectedNodeType
        {
            get => selectedNodeType;
            set => SetProperty(ref selectedNodeType, value);
        }

        public int[] FetchCounts => new int[] { 10, 25, 50, 100, 250, 500, 1000, 5000 };
        public int FetchCount { get; set; } = 10;
        public string? StartOffset { get; set; }
        public DateTime StartTimestamp { get; set; } = DateTime.Now.Subtract(TimeSpan.FromMinutes(1));
        public DateTime EndTimestamp { get; set; } = DateTime.Now;

        private int fontSize = 14;
        private string fetchPosition;

        public int FontSize
        {
            get => fontSize;
            set => SetProperty(ref fontSize, value, true);
        }

        public string FetchPosition
        {
            get => fetchPosition;
            set => SetProperty(ref fetchPosition, value);
        }

        public OpenedClusterViewModel(
            ISettingsService settingsService,
            IClusterService clusterService,
            ClusterViewModel clusterViewModel,
            string name)
        {
            this.settingsService = settingsService;
            this.clusterService = clusterService;
            this.clusterViewModel = clusterViewModel;
            Name = name;

            FetchMessagesCommand = new RelayCommand(FetchMessages);
            ChangeFormatterCommand = new AsyncRelayCommand(UpdateFormatterAsync);

            FetchPosition = FetchPositionsForTopic[0];
            Nodes.Add(this);
            IsSelected = true;
            IsExpanded = true;

            IsActive = true;
        }

        private Task UpdateFormatterAsync()
        {
            throw new NotImplementedException();
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
                Topics.Add(new TopicViewModel(clusterService, topic, jsonFormatter));
            }
        }

        public ITreeNode? SelectedNode
        {
            get => selectedNode;
            set
            {
                if (SetProperty(ref selectedNode, value))
                {
                    FetchPositions = SelectedNodeType == ITreeNode.NodeType.PARTITION
                        ? FetchPositionsForPartition
                        : FetchPositionsForTopic;
                    if (selectedNode is { Type: ITreeNode.NodeType.PARTITION } or { Type: ITreeNode.NodeType.TOPIC })
                    {
                        FetchMessagesCommand.Execute(null);
                    }
                    SelectedNodeType = selectedNode?.Type ?? ITreeNode.NodeType.NONE;
                }
            }
        }

        public string ClusterId => clusterViewModel.Id;
        MessageStream? messages = null;
        private readonly DispatcherTimer timer = new()
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };

        private void FetchMessages()
        {
            if (selectedNode == null)
            {
                return;
            }

            var fetchOptions = CreateFetchOptions();

            timer.Start();
            timer.Tick += OnDispatcherTimer_Tick;
            timer.Start();

            messages = selectedNode switch
            {
                TopicViewModel topic => clusterService.GetMessagesAsync(clusterViewModel.Id, topic.Name,
                    fetchOptions),

                PartitionViewModel partition => clusterService.GetMessagesAsync(clusterViewModel.Id,
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
                foreach (var msg in e.NewItems)
                {
                    MessageViewModel viewModel = new((Message)msg, formatter);
                    pendingMessages.Add(viewModel);
                }
                Log.Debug("Pending messages = {Count}", pendingMessages.Count);
            }
        }

        private void OnMessagesFinished()
        {
            timer.Tick -= OnDispatcherTimer_Tick;
            timer.Stop();
        }

        private void OnDispatcherTimer_Tick(object? sender, EventArgs e)
        {
            lock (pendingMessages)
            {
                Log.Debug("UI: Pending messages = {Count}", pendingMessages.Count);
                pendingMessages.ForEach(CurrentMessages.Add);
                pendingMessages.Clear();
            }

            if (!messages?.HasMore ?? false)
            {
                Log.Debug("UI: No more messages");
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
                    end = Core.Services.FetchPosition.END;
                    start = new(PositionType.OFFSET, Core.Services.FetchPosition.END.Offset - FetchCount);
                    break;
                case "Start":
                    start = Core.Services.FetchPosition.START;
                    break;
                case "Timestamp":
                    var epochMs = (long)(StartTimestamp.ToUniversalTime() - new DateTime(1970, 1, 1)).TotalMilliseconds;
                    start = new(PositionType.TIMESTAMP, epochMs);
                    //end = new(PositionType.TIMESTAMP, DateTimeOffset.Now.ToUnixTimeSeconds());
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

        private void OnMessagesFetched(object source, List<Message> messages)
        {
            var node = (IMessageSource)source;
            var formatter = node.Formatter;
            foreach (var msg in messages)
            {
                MessageViewModel viewModel = new(msg, formatter);
                CurrentMessages.Add(viewModel);
            }
        }
    }
}
