namespace KafkaLens.Messages
{
    public class OpenClusterMessage
    {
        public ClusterViewModel ClusterViewModel { get; }
        public OpenClusterMessage(ClusterViewModel clusterViewModel)
        {
            ClusterViewModel = clusterViewModel;
        }
    }
}
