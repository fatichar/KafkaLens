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
    
    [ObservableProperty]
    private bool isSelected;
    [ObservableProperty]
    private bool isExpanded;

    [ObservableProperty] public List<IMessageFormatter> formatters;
    [ObservableProperty] private string? formatterName;
    [ObservableProperty] private string? keyFormatterName;

    public ObservableCollection<MessageViewModel> Messages { get; } = new();

    public ITreeNode.NodeType Type => ITreeNode.NodeType.TOPIC;

    public TopicViewModel(Topic topic, string? formatterName, string? keyFormatterName)
    {
        formatters = FormatterFactory.Instance.GetFormatters();
        this.topic = topic;
        FormatterName = formatterName;
        KeyFormatterName = keyFormatterName;
        foreach (var partition in topic.Partitions)
        {
            var viewModel = new PartitionViewModel(this, partition);
            Partitions.Add(viewModel);
            Children.Add(viewModel);
        }

        IsActive = true;
    }
}