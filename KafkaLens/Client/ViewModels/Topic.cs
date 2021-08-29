using System.Collections.Generic;
using KafkaLens.Client.AppConsatnts;

namespace KafkaLens.Client.ViewModels
{
    public class Topic : INode
    {
        public Topic(string name, int partitionCount, string parentId)
        {
            Id = parentId + AppConstants.ID_SEPARATOR + name;
            Name = name;
            Children = new List<INode>(partitionCount);
            for (int i = 0; i < partitionCount; i++)
            {
                Children.Add(new Partition(i, Id));
            }
        }

        public string Id { get; }
        public string Name { get; }
        public int PartitionCount => Children?.Count ?? 0;
        public IList<INode> Children { get; }
        public bool Expanded { get; set; } = false;
        public INode.NodeType Type => INode.NodeType.TOPIC;
    }
}
