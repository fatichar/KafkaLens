using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KafkaLens.App.Formating;
using KafkaLens.Core.Services;
using KafkaLens.Shared.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace KafkaLens.App.ViewModels
{
    public sealed class OpenedClusterViewModel : ObservableRecipient
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

        static OpenedClusterViewModel()
        {
            formatters.Add(textFormatter.Name, textFormatter);
            formatters.Add(jsonFormatter.Name, jsonFormatter);

            FetchPositionsForTopic.Add("End");
            FetchPositionsForTopic.Add("Timestamp");
            FetchPositionsForTopic.Add("Start");

            FetchPositionsForPartition.Add("End");
            FetchPositionsForPartition.Add("Timestamp");
            FetchPositionsForPartition.Add("Offset");
            FetchPositionsForPartition.Add("Start");
        }

        public ICollection<string> MessageFormats => formatters.Keys;

        public IAsyncRelayCommand FetchMessagesCommand { get; }
        public IAsyncRelayCommand ChangeFormatterCommand { get; }

        public string Name { get; }
        public ObservableCollection<TopicViewModel> Topics { get; } = new();

        public MessagesViewModel CurrentMessages { get; } = new();

        private IMessageSource? selectedNode;

        public int[] FetchCounts => new int[] { 10, 25, 50, 100, 250, 500, 1000, 5000 };
        public int FetchCount { get; set; } = 10;

        public string FetchPosition { get; set; }
        public String StartOffset { get; set; }

        private int fontSize = 14;
        public int FontSize
        {
            get => fontSize;
            set
            {
                SetProperty(ref fontSize, value, true);
            }
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

            FetchMessagesCommand = new AsyncRelayCommand(FetchMessagesAsync);
            ChangeFormatterCommand = new AsyncRelayCommand(UpdateFormatterAsync);

            FetchPosition = FetchPositionsForTopic[0];

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

        public IMessageSource? SelectedNode
        {
            get => selectedNode;
            set
            {
                if (SetProperty(ref selectedNode, value))
                {
                    FetchPositions = value is PartitionViewModel
                        ? FetchPositionsForPartition
                        : FetchPositionsForTopic;
                    if (selectedNode != null)
                    {
                        FetchMessagesCommand.Execute(null);
                    }
                }
            }
        }

        public string ClusterId => clusterViewModel.Id;

        private async Task FetchMessagesAsync()
        {
            if (selectedNode == null)
            {
                return;
            }
            List<Message>? messages = null;
            var fetchOptions = CreateFetchOptions();
            if (selectedNode is TopicViewModel topic)
            {
                messages = await clusterService.GetMessagesAsync(clusterViewModel.Id, topic.Name, fetchOptions);
            }
            else if (selectedNode is PartitionViewModel partition)
            {
                messages = await clusterService.GetMessagesAsync(clusterViewModel.Id, partition.TopicName, partition.Id, fetchOptions);
            }

            if (messages != null)
            {
                CurrentMessages.Clear();
                // TODO: fetch messages in multiple steps
                OnMessagesFetched(selectedNode, messages);
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
                    start = new(PositionType.TIMESTAMP, DateTimeOffset.Now.ToUnixTimeSeconds() - 60);
                    end = new(PositionType.TIMESTAMP, DateTimeOffset.Now.ToUnixTimeSeconds());
                    break;
                case "Offset":
                    start = new(PositionType.OFFSET, long.TryParse(StartOffset, out long offset) ? offset : -1);                    
                    break;
                default:
                    throw new Exception("Invalid fetch position " + FetchPosition);
            }
            var fetchOptions = new FetchOptions(start, end);
            fetchOptions.Limit = FetchCount;
            return fetchOptions;
        }

        private void OnMessagesFetched(IMessageSource node, List<Message> messages)
        {
            if (node != null)
            {
                var formatter = node.Formatter;
                foreach (var msg in messages)
                {
                    MessageViewModel viewModel = new(msg, formatter);
                    CurrentMessages.Add(viewModel);
                }
            }
        }
    }
}
