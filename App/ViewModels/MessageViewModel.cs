using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging.Messages;
using CommunityToolkit.Mvvm.Messaging;
using KafkaLens.Shared.Models;
using System;

namespace KafkaLens.App.ViewModels
{
    public sealed class MessageViewModel : ObservableRecipient //, IRecipient<PropertyChangedMessage<Message>>
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

            IsActive = true;
        }
    }
}
