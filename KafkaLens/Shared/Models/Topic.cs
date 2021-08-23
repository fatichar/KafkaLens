using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KafkaLens.Shared.Models
{
    public class Topic
    {
        public Topic(string name, int partitions)
        {
            Name = name;
            Partitions = new List<Partition>(partitions);
            for (int i = 0; i < partitions; i++)
            {
                Partitions.Add(new Partition(i));
            }
        }

        public string Name { get; }
        public int ParitionCount => Partitions?.Count ?? 0;
        public List<Partition> Partitions { get; set; }
    }
}
