using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace KafkaLens.Shared.Models
{
    public class Topic
    {
        //public Topic()
        //{
        //    //Partitions = new List<Partition>();
        //}

        public Topic(string name, int partitionCount)
        {
            Name = name;
            PartitionCount = partitionCount;
            //Partitions = new List<Partition>(partitions);
            //for (int i = 0; i < partitions; i++)
            //{
            //    Partitions.Add(new Partition(i));
            //}
        }

        public string Name { get; set; }

        public int PartitionCount { get; }

        public string ParentId { get; set; }

        //public List<Partition> Partitions { get; }
    }
}
