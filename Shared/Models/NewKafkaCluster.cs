namespace KafkaLens.Shared.Models
{
    public class NewKafkaCluster
    {
        public NewKafkaCluster(string name, string bootstrapServers)
        {
            Name = name;
            BootstrapServers = bootstrapServers;
        }

        public string Name { get; set; }
        public string BootstrapServers { get; set; }
    }
}