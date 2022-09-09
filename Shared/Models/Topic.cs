using System.Collections.Generic;

namespace KafkaLens.Shared.Models
{
    public class Topic
    {
        public Topic(string name, int partitionCount)
        {
            Name = name;
            Partitions = new List<Partition>(partitionCount);
            for (var i = 0; i < partitionCount; i++)
            {
                Partitions.Add(new Partition(i));
            }
        }

        public string Name { get; set; }

        public int PartitionCount => Partitions.Count;
        public List<Partition> Partitions { get; }

        //public string ParentId { get; set; }
    }
}
