using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using KafkaLens.Shared.Models;

namespace KafkaLens.ViewModels;

public partial class PartitionViewModel: ViewModelBase, IMessageSource {
    private readonly Partition partition;
    public int Id => partition.Id;
    public string Name => partition.Name;
    private readonly TopicViewModel topic;

    public string TopicName => topic.Name;
    public bool IsExpandable => false;
    [ObservableProperty]
    private bool isSelected;
    [ObservableProperty]
    private bool isExpanded;
    public ObservableCollection<ITreeNode> Children { get; } = new();

    public ITreeNode.NodeType Type => ITreeNode.NodeType.Partition;

    public PartitionViewModel(TopicViewModel topic, Partition partition) {
        this.partition = partition;
        this.topic = topic;
        this.topic.PropertyChanged += (s, e) => {
            if (e.PropertyName == nameof(TopicViewModel.FormatterName)) OnPropertyChanged(nameof(FormatterName));
            if (e.PropertyName == nameof(TopicViewModel.KeyFormatterName)) OnPropertyChanged(nameof(KeyFormatterName));
        };

        IsActive = true;
    }

    protected override void OnActivated() {
        Messenger.Register<PartitionViewModel, PropertyChangedMessage<TopicPartition>>(this, (r, m) => r.Receive(m));
    }

    public ObservableCollection<MessageViewModel> Messages { get; } = new();

    public string? FormatterName
    {
        get => topic.FormatterName;
        set => topic.FormatterName = value;
    }

    public string? KeyFormatterName
    {
        get => topic.KeyFormatterName;
        set => topic.KeyFormatterName = value;
    }

    public void Receive(PropertyChangedMessage<TopicPartition> message) {
    }
}