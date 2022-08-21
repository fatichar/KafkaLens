using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging.Messages;
using CommunityToolkit.Mvvm.Messaging;
using KafkaLens.Shared.Models;
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
        public bool IsExpandable => true;
        public bool IsExpanded { get; set; }
        public bool IsSelected { get; set; }

        public ObservableCollection<MessageViewModel> Messages { get; } = new();

        public TopicViewModel(IClusterService clusterService, Topic topic)
        {
            this.clusterService = clusterService;
            this.topic = topic;

            foreach (var parittion in topic.Partitions)
            {
                Partitions.Add(new PartitionViewModel(clusterService, this, parittion));
            }

            IsActive = true;
        }
    }
}
