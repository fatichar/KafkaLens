using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging.Messages;
using CommunityToolkit.Mvvm.Messaging;
using KafkaLens.Core.Services;
using KafkaLens.Shared.Models;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace KafkaLens.App.ViewModels
{
    public class PartitionViewModel : ObservableRecipient
    {
        private readonly IClusterService clusterService;
        private readonly Partition partition;
        public string Name => partition.Name;

        public PartitionViewModel(IClusterService clusterService, Partition partition)
        {
            LoadMessagesCommand = new AsyncRelayCommand(LoadMessagesAsync);
            this.clusterService = clusterService;
            this.partition = partition;
            
            IsActive = true;
        }

        protected override void OnActivated()
        {
            // We use a method group here, but a lambda expression is also valid
            Messenger.Register<PartitionViewModel, PropertyChangedMessage<TopicPartition>>(this, (r, m) => r.Receive(m));
        }

        public IAsyncRelayCommand LoadMessagesCommand { get; }
        public ObservableCollection<MessageViewModel> Messages { get; } = new();
        
        private Task LoadMessagesAsync()
        {
            throw new NotImplementedException();
        }

        public void Receive(PropertyChangedMessage<TopicPartition> message)
        {
            if (message.Sender.GetType() == typeof(OpenedClusterViewModel) &&
                    message.PropertyName == nameof(OpenedClusterViewModel.SelectedTopic))
            {
            }
        }
    }
}