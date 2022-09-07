namespace KafkaLens.App.ViewModels
{
    public interface ITreeNode
    {
        enum NodeType
        {
            CLUSTER,
            TOPIC,
            PARTITION,
            NONE
        }
        bool IsSelected { get; set; }
        string Name { get; }
        NodeType Type { get; }
    }
}