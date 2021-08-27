using System.Collections.Generic;

namespace KafkaLens.Client.ViewModels
{
    public class Partition : INode
    {
        public Partition(int id, string parentId)
        {
            Id = id;
            ParentId = parentId;
        }

        public int Id { get; }
        public string Name => Id.ToString();
        public string ParentId { get; }
        public bool Expanded { get; set; }
        public IList<INode> Children => null;

        //public int MaxOffset { get; set; }
    }
}