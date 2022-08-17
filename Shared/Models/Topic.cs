namespace KafkaLens.Shared.Models
{
    public class Topic
    {
        public Topic(string name, int partitionCount)
        {
            Name = name;
            PartitionCount = partitionCount;
        }

        public string Name { get; set; }

        public int PartitionCount { get; }

        //public string ParentId { get; set; }
    }
}
