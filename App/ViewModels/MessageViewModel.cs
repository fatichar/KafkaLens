﻿using CommunityToolkit.Mvvm.ComponentModel;
using KafkaLens.Shared.Models;
using System;

namespace KafkaLens.App.ViewModels
{
    public sealed class MessageViewModel : ObservableRecipient
    {
        private readonly Message message;

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