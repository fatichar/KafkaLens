using System.Collections.ObjectModel;

namespace KafkaLens.ViewModels;

public interface ITreeNode
{
    enum NodeType
    {
        CLUSTER,
        TOPIC,
        PARTITION,
        NONE
    }
    string Name { get; }
    NodeType Type { get; }
    bool IsExpanded { get; set; }
    bool IsSelected { get; set; }
    ObservableCollection<ITreeNode> Children { get; }
}