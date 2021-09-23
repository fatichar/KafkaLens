using System.Collections.Generic;

namespace KafkaLens.Client.ViewModels
{
    public class KafkaCluster : INode
    {
        private bool expanded;

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
        public bool CanSelect => true;
        public INode.NodeType Type => INode.NodeType.CLUSTER;
        public bool Selected { get; set; }
    }
}