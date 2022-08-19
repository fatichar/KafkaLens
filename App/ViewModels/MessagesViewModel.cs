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
    public sealed class MessagesViewModel : ObservableRecipient
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
        protected override void OnActivated()
        {
            // We use a method group here, but a lambda expression is also valid
            Messenger.Register<MessagesViewModel, PropertyChangedMessage<TopicPartition>>(this, (r, m) => r.Receive(m));
        }

        private Task LoadMessagesAsync()
        {
            throw new NotImplementedException();
        }

        public void Receive(PropertyChangedMessage<TopicPartition> message)
        {
            if (message.Sender.GetType() == typeof(OpenedClusterViewModel) &&
                    message.PropertyName == nameof(OpenedClusterViewModel.SelectedTopic))
            {
                TopicPartition = (TopicPartition?)message.NewValue;

                LoadMessagesAsync();
            }
        }

        public TopicPartition? TopicPartition
        {
            get => topicPartition;
            set => SetProperty(ref topicPartition, value);
        }
    }
}
