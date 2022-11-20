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
    public bool IsExpanded { get; }
    bool IsSelected { get; set; }
}