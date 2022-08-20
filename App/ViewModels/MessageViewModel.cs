﻿using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging.Messages;
using CommunityToolkit.Mvvm.Messaging;
using KafkaLens.Shared.Models;

namespace KafkaLens.App.ViewModels
{
    public sealed class MessageViewModel : ObservableRecipient //, IRecipient<PropertyChangedMessage<Message>>
    {
        private Message message;

        public MessageViewModel(Message message)
        {
            this.message = message;
        }

        public Message Message
        {
            get => message;
            private set => SetProperty(ref message, value);
        }

        public void Receive(PropertyChangedMessage<Message> message)
        {
            //if (message.Sender.GetType() == typeof(MessagesViewModel) &&
            //    message.PropertyName == nameof(MessagesViewModel.CurrentMessage))
            //{
            //    Message = message.NewValue;
            //}
        }
    }
}
