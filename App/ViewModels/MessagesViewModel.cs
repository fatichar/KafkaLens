using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging.Messages;
using CommunityToolkit.Mvvm.Messaging;
using KafkaLens.Shared.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;

namespace KafkaLens.App.ViewModels
{
    public sealed class MessagesViewModel : ObservableRecipient//, IRecipient<PropertyChangedMessage<object>>
    {
        private TopicPartition? topicPartition;
        public IAsyncRelayCommand LoadMessagesCommand { get; }

        public List<Message> Messages { get; internal set; }
        public Message? CurrentMessage { get; internal set; }
        public List<Message> SelectedMessages { get; internal set; }

        public MessagesViewModel()
        {
            Messages = new List<Message>();
            SelectedMessages = new List<Message>();
            LoadMessagesCommand = new AsyncRelayCommand(LoadMessagesAsync);
        }

        private Task LoadMessagesAsync()
        {
            throw new NotImplementedException();
        }

        public TopicPartition? TopicPartition
        {
            get => topicPartition;
            set => SetProperty(ref topicPartition, value);
        }

        public void Receive(PropertyChangedMessage<object> topicPartition)
        {
            if (topicPartition.Sender.GetType() == typeof(ClusterViewModel) &&
                topicPartition.PropertyName == nameof(ClusterViewModel.SelectedTopic))
            {
                TopicPartition = (TopicPartition?)topicPartition.NewValue;

                LoadMessagesAsync();
            }
        }
    }
}
