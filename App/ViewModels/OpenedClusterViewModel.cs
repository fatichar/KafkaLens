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

        private TopicViewModel? selectedTopic;

        
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

        public TopicViewModel? SelectedTopic
        {
            get => selectedTopic;
            set
            {
                SetProperty(ref selectedTopic, value, true);

                settingsService.SetValue(nameof(SelectedTopic), value.Name);
                if (selectedTopic != null)
                {
                    FetchMessagesCommand.Execute(null);
                }
            }
        }

       
        public string ClusterId => clusterViewModel.Id;

        private async Task FetchMessagesAsync()
        {
            if (selectedTopic == null)
            {
                return;
            }
            // load messages
            var messages = await clusterService.GetMessagesAsync(
                clusterViewModel.Id,
                selectedTopic.Name, new FetchOptions()
                {
                    Limit = 10
                });
            OnMessagesFetched(messages);
        }

        private void OnMessagesFetched(List<Message> messages)
        {
            if (selectedTopic == null)
            {
                CurrentMessages.Messages.Clear();
            }
            else
            {
                foreach (var msg in messages)
                {
                    CurrentMessages.Messages.Add(new MessageViewModel(msg));
                }
            }
        }
    }
}
