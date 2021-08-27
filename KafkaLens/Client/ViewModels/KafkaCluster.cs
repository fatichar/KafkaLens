using System.Collections.Generic;

namespace KafkaLens.Client.ViewModels
{
    public class KafkaCluster : INode
    {
        public KafkaCluster(string id, string name, string bootstrapServers)
        {
            Id = id;
            Name = name;
            BootstrapServers = bootstrapServers;
        }

        public string Id { get; set; }
        public string Name { get; set; }
        public string BootstrapServers { get; set; }

        public IList<INode> Children { get; set; }
        public bool Expanded { get; set; }
    }
}