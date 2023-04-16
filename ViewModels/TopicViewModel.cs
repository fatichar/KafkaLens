using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using KafkaLens.Shared.Models;
using KafkaLens.Formatting;

namespace KafkaLens.ViewModels;

public partial class TopicViewModel: ViewModelBase, IMessageSource
{
    private readonly Topic topic;
    public ObservableCollection<PartitionViewModel> Partitions { get; } = new();
    public ObservableCollection<ITreeNode> Children { get; } = new();

    public string Name => topic.Name;
    public bool IsExpandable => true;
    public bool IsExpanded { get; set; }
    public bool IsSelected { get; set; }

    [ObservableProperty] public List<IMessageFormatter> formatters;
    public string? FormatterName { get; set; }

    public ObservableCollection<MessageViewModel> Messages { get; } = new();

    public ITreeNode.NodeType Type => ITreeNode.NodeType.TOPIC;

    public TopicViewModel(Topic topic, string? formatterName)
    {
        formatters = FormatterFactory.Instance.GetFormatters();
        this.topic = topic;
        FormatterName = formatterName;
        foreach (var partition in topic.Partitions)
        {
            var viewModel = new PartitionViewModel(this, partition);
            Partitions.Add(viewModel);
            Children.Add(viewModel);
        }

        IsActive = true;
    }
}