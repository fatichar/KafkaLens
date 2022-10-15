using System.Collections.Generic;

namespace KafkaLens.Client.ViewModels;

public interface INode
{
    enum NodeType
    {
        CLUSTER,
        TOPIC,
        PARTITION
    }


    public NodeType Type { get; }
    public string Id { get; }
    public string Name { get; }
    IList<INode> Children { get; }
    bool HasChildren => (Children?.Count ?? 0) > 0;
    bool Expanded { get; set; }
    bool Selected { get; set; }
    bool CanSelect { get; }
}