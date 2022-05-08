namespace KafkaLens.Shared.Models
{
    public class Partition
    {
        public Partition(int id)
        {
            Id = id;
        }

        public int Id { get; set; }
        public int MaxOffset { get; set; }
    }
}