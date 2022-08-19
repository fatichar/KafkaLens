namespace KafkaLens.Shared.Models
{
    public class KafkaClusterUpdate
    {
        public KafkaClusterUpdate(string name, string bootstrapServers)
        {
            Name = name;
            BootstrapServers = bootstrapServers;
        }

        public string Name { get; set; }
        public string BootstrapServers { get; set; }
    }
}