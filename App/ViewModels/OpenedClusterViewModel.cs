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
        public ObservableCollection<MessageViewModel> CurrentMessages = new();


        public TopicViewModel? selectedTopic;

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

                settingsService.SetValue(nameof(SelectedTopic), value);
                if (selectedTopic != null)
                {
                    FetchMessagesCommand.ExecuteAsync(null);
                }
            }
        }

        private async Task FetchMessagesAsync()
        {
            if (selectedTopic == null)
            {
                return;
            }
            // load messages
            var messagesTask = clusterService.GetMessagesAsync(
                clusterViewModel.Id,
                selectedTopic.Name, new FetchOptions()
                {
                    Limit = 10
                });
            await messagesTask.ContinueWith(OnMessagesFetched);
        }

        private void OnMessagesFetched(Task task)
        {
            if (selectedTopic == null)
            {
                CurrentMessages.Clear();
            }
            else
            {
                foreach (var msg in selectedTopic.Messages)
                {
                    CurrentMessages.Add(msg);
                }
            }
        }
    }
}
