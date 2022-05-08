using System.Collections.Generic;
using KafkaLens.Client.AppConsatnts;

namespace KafkaLens.Client.ViewModels
{
    public class Partition : INode
    {
        public Partition(int number, string parentId)
        {
            Number = number;
            Id = parentId + AppConstants.ID_SEPARATOR + number.ToString();
            ParentId = parentId;
        }

        public string Id { get; }
        public int Number { get; }
        public string Name => "Partition_" + Number.ToString();
        public string ParentId { get; }
        public bool Expanded { get; set; }
        public bool Selected { get; set; }
        public bool CanSelect => true;
        public IList<INode> Children => null;
        public INode.NodeType Type => INode.NodeType.PARTITION;

        //public int MaxOffset { get; set; }
    }
}