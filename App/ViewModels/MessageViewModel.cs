using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging.Messages;
using CommunityToolkit.Mvvm.Messaging;
using KafkaLens.Shared.Models;
using System;

namespace KafkaLens.App.ViewModels
{
    public sealed class MessageViewModel //, IRecipient<PropertyChangedMessage<Message>>
    {
        private Message message;

        public int Partition => message.Partition;
        public long Offset => message.Offset;
        public string Key => message.KeyText;
        public string Summary => message.ValueText.Substring(0, 100);
        public string Message => message.ValueText;
        public DateTime Timestamp => DateTime.UnixEpoch.AddMilliseconds(message.EpochMillis).ToLocalTime();


        public MessageViewModel(Message message)
        {
            this.message = message;
        }

        //public Message Message
        //{
        //    get => message;
        //    private set => SetProperty(ref message, value);
        //}

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
