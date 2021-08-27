using System.Collections.Generic;

namespace KafkaLens.Client.ViewModels
{
    public class Topic : INode
    {
        public Topic(string name, int partitionCount, string parentId)
        {
            Name = name;
            Children = new List<INode>(partitionCount);
            for (int i = 0; i < partitionCount; i++)
            {
                Children.Add(new Partition(i, Name));
            }
        }

        public string Name { get; }
        public int PartitionCount => Children?.Count ?? 0;
        public IList<INode> Children { get; }
        public bool Expanded { get; set; } = false;
    }
}
