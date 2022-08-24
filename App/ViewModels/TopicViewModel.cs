using CommunityToolkit.Mvvm.ComponentModel;
using KafkaLens.Shared.Models;
using KafkaLens.Core.Services;
using System.Collections.ObjectModel;
using KafkaLens.App.Formating;

namespace KafkaLens.App.ViewModels
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
