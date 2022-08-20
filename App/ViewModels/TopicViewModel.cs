﻿using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging.Messages;
using CommunityToolkit.Mvvm.Messaging;
using KafkaLens.Shared.Models;
using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using KafkaLens.Core.Services;
using System.Collections.ObjectModel;

namespace KafkaLens.App.ViewModels
{
    public sealed class TopicViewModel : ObservableRecipient
    {
        private readonly IClusterService clusterService;
        private readonly Topic topic;
        public ObservableCollection<PartitionViewModel> Partitions { get; } = new();

        public string Name => topic.Name;

        public ObservableCollection<MessageViewModel> Messages { get; } = new();

        public TopicViewModel(IClusterService clusterService, Topic topic)
        {
            this.clusterService = clusterService;
            this.topic = topic;

            foreach (var parittion in topic.Partitions)
            {
                Partitions.Add(new PartitionViewModel(clusterService, parittion));
            }
            
            IsActive = true;
        }

        protected override void OnActivated()
        {
            // We use a method group here, but a lambda expression is also valid
            Messenger.Register<TopicViewModel, PropertyChangedMessage<TopicPartition>>(this, (r, m) => r.Receive(m));
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
