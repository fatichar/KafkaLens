using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using KafkaLens.Core.Services;
using KafkaLens.Formatting;
using KafkaLens.Shared.Models;

namespace KafkaLens.ViewModels
{
    public sealed class TopicViewModel : ObservableRecipient, IMessageSource
    {
        private readonly IClusterService clusterService;
        private readonly Topic topic;
        public ObservableCollection<PartitionViewModel> Partitions { get; } = new();

        public string Name => topic.Name;
        public bool IsExpandable => true;
        public bool IsExpanded { get; set; }
        public bool IsSelected { get; set; }
        public IMessageFormatter Formatter { get; set; }

        public ObservableCollection<MessageViewModel> Messages { get; } = new();

        public ITreeNode.NodeType Type => ITreeNode.NodeType.TOPIC;

        public TopicViewModel(IClusterService clusterService, Topic topic, IMessageFormatter formatter)
        {
            this.clusterService = clusterService;
            this.topic = topic;
            Formatter = formatter;
            foreach (var parittion in topic.Partitions)
            {
                Partitions.Add(new PartitionViewModel(clusterService, this, parittion));
            }

            IsActive = true;
        }
    }
}
