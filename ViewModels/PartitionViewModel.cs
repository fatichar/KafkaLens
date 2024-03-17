using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using KafkaLens.Shared.Models;
using KafkaLens.Formatting;

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

    public ITreeNode.NodeType Type => ITreeNode.NodeType.PARTITION;

    public PartitionViewModel(TopicViewModel topic, Partition partition) {
        LoadMessagesCommand = new AsyncRelayCommand(LoadMessagesAsync);
        this.partition = partition;
        this.topic = topic;

        IsActive = true;
    }

    protected override void OnActivated() {
        // We use a method group here, but a lambda expression is also valid
        Messenger.Register<PartitionViewModel, PropertyChangedMessage<TopicPartition>>(this, (r, m) => r.Receive(m));
    }

    public IAsyncRelayCommand LoadMessagesCommand { get; }
    public ObservableCollection<MessageViewModel> Messages { get; } = new();

    public string FormatterName
    {
        get => topic.FormatterName;
        set => topic.FormatterName = value;
    }

    private Task LoadMessagesAsync() {
        throw new NotImplementedException();
    }

    public void Receive(PropertyChangedMessage<TopicPartition> message) {
    }
}