using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using CommunityToolkit.Mvvm.Messaging.Messages;
using KafkaLens.Shared;
using KafkaLens.Shared.Models;
using KafkaLens.Formatting;

namespace KafkaLens.ViewModels;

public class PartitionViewModel: ViewModelBase, IMessageSource {
    private readonly IKafkaLensClient kafkaLensClient;
    private readonly Partition partition;
    public int Id => partition.Id;
    public string Name => partition.Name;
    private readonly TopicViewModel topic;

    public string TopicName => topic.Name;
    public bool IsExpandable => false;
    public bool IsExpanded { get; set; }
    public bool IsSelected { get; set; }
    public ObservableCollection<ITreeNode> Children { get; } = new();

    public ITreeNode.NodeType Type => ITreeNode.NodeType.PARTITION;

    public PartitionViewModel(IKafkaLensClient kafkaLensClient, TopicViewModel topic, Partition partition) {
        LoadMessagesCommand = new AsyncRelayCommand(LoadMessagesAsync);
        this.kafkaLensClient = kafkaLensClient;
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