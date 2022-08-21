using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging.Messages;
using CommunityToolkit.Mvvm.Messaging;
using KafkaLens.Shared.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;

namespace KafkaLens.App.ViewModels
{
    public sealed class MessagesViewModel : ObservableRecipient
    {
        private TopicPartition? topicPartition;

        private MessageViewModel? currentMessage;
        public IAsyncRelayCommand LoadMessagesCommand { get; }

        public ObservableCollection<MessageViewModel> Messages { get; internal set; }

        public ObservableCollection<MessageViewModel> SelectedMessages { get; internal set; }

        public MessagesViewModel()
        {
            Messages = new();
            SelectedMessages = new();
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
        }

        public TopicPartition? TopicPartition
        {
            get => topicPartition;
            set => SetProperty(ref topicPartition, value);
        }

        public MessageViewModel? CurrentMessage
        {
            get => currentMessage;
            set => SetProperty(ref currentMessage, value);
        }
    }
}
