using System.Collections.ObjectModel;

namespace KafkaLens.ViewModels;

public interface ITreeNode
{
    enum NodeType
    {
        Cluster,
        Topic,
        Partition,
        None
    }
    string Name { get; }
    NodeType Type { get; }
    bool IsExpanded { get; set; }
    bool IsSelected { get; set; }
    ObservableCollection<ITreeNode> Children { get; }

    /// <summary>Whether this node can be included in a multi-node fetch. Topics only.</summary>
    bool IsCheckable => false;

    /// <summary>Included in the multi-topic fetch. Meaningful only when <see cref="IsCheckable"/>.</summary>
    bool IsChecked
    {
        get => false;
        set { }
    }
}