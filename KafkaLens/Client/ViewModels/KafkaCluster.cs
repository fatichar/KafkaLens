using System.Collections.Generic;

namespace KafkaLens.Client.ViewModels
{
    public class KafkaCluster : INode
    {
        public KafkaCluster(string name, string bootstrapServers)
        {
            Name = name;
            BootstrapServers = bootstrapServers;
        }

        public string Id => Name;
        public string Name { get; set; }
        public string BootstrapServers { get; set; }

        public IList<INode> Children { get; set; }
        public bool Expanded { get; set; }
        public INode.NodeType Type => INode.NodeType.CLUSTER;
    }
}