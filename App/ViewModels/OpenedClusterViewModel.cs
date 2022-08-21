using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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

        IAsyncRelayCommand FetchMessagesCommand { get; }

        public string Name => clusterViewModel.Name;
        public ObservableCollection<TopicViewModel> Topics => clusterViewModel.Topics;

        public MessagesViewModel CurrentMessages { get; }  = new();

        // TODO create interface for nodes
        private object? selectedNode;

        public OpenedClusterViewModel(
            ISettingsService settingsService,
            IClusterService clusterService,
            ClusterViewModel clusterViewModel)
        {
            this.settingsService = settingsService;
            this.clusterService = clusterService;
            this.clusterViewModel = clusterViewModel;

            FetchMessagesCommand = new AsyncRelayCommand(FetchMessagesAsync);

            //var selectedTopicName = settingsService.GetValue<string>(nameof(SelectedTopic));
            IsActive = true;
        }

        public object? SelectedNode
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
                messages = await clusterService.GetMessagesAsync(clusterViewModel.Id, topic.Name, new FetchOptions(){Limit = 10});
            }
            else if (selectedNode is PartitionViewModel partition)
            {
                messages = await clusterService.GetMessagesAsync(clusterViewModel.Id, partition.TopicName, partition.Id, new FetchOptions(){Limit = 10});
            }

            if (messages != null)
            {
                CurrentMessages.Messages.Clear();
                // TODO: fetch messages in multiple steps
                OnMessagesFetched(messages);
            }
        }

        private void OnMessagesFetched(List<Message> messages)
        {
            if (selectedNode != null)
            {
                foreach (var msg in messages)
                {
                    CurrentMessages.Messages.Add(new MessageViewModel(msg));
                }
            }
        }
    }
}
