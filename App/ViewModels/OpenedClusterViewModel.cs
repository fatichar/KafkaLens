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

        IAsyncRelayCommand FetchMessagesCommand { get; }
        IAsyncRelayCommand ChangeFormatterCommand { get; }

        public string Name { get; }
        public ObservableCollection<TopicViewModel> Topics { get; } = new();

        public MessagesViewModel CurrentMessages { get; } = new();

        private IMessageSource? selectedNode;

        public int[] FetchCounts => new int[] { 10, 25, 50, 100, 250, 500, 1000, 5000 };
        public int FetchCount { get; set; } = 10;

        public FetchOptions.FetchPosition FetchPosition { get; set; } = FetchOptions.FetchPosition.END;

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
                SetProperty(ref selectedNode, value, true);

                if (selectedNode != null)
                {
                    FetchMessagesCommand.Execute(null);
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
            if (selectedNode is TopicViewModel topic)
            {
                messages = await clusterService.GetMessagesAsync(clusterViewModel.Id, topic.Name, new FetchOptions(FetchPosition, FetchCount));
            }
            else if (selectedNode is PartitionViewModel partition)
            {
                messages = await clusterService.GetMessagesAsync(clusterViewModel.Id, partition.TopicName, partition.Id, new FetchOptions(FetchPosition, FetchCount));
            }

            if (messages != null)
            {
                CurrentMessages.Messages.Clear();
                // TODO: fetch messages in multiple steps
                OnMessagesFetched(selectedNode, messages);
            }
        }

        private void OnMessagesFetched(IMessageSource node, List<Message> messages)
        {
            if (node != null)
            {
                var formatter = node.Formatter;
                foreach (var msg in messages)
                {
                    MessageViewModel viewModel = new MessageViewModel(msg, formatter);
                    CurrentMessages.Messages.Add(viewModel);
                }
            }
        }

        private static IDictionary<string, IMessageFormatter?> formatters = new Dictionary<string, IMessageFormatter?>();

        private static IMessageFormatter jsonFormatter = new JsonFormatter();
        private static IMessageFormatter textFormatter = new TextFormatter();
        static OpenedClusterViewModel()
        {
            formatters.Add(textFormatter.Name, textFormatter);
            formatters.Add(jsonFormatter.Name, jsonFormatter);
        }

        public ICollection<string> MessageFormats => formatters.Keys;
    }
}
