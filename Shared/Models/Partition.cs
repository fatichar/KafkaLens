namespace KafkaLens.Shared.Models
{
    public class Partition
    {
        public Partition(int id)
        {
            Id = id;
            Name = "Partition " + Id;
        }

        public int Id { get; }
        public string Name { get; }
    }
}