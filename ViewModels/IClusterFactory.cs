namespace KafkaLens.ViewModels;

public interface IClusterFactory
{
    public Task LoadClustersAsync();
    public List<ClusterViewModel> GetAllClusters();
}