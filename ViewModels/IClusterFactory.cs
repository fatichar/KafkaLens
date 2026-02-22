namespace KafkaLens.ViewModels;

public interface IClusterFactory
{
    public Task<IReadOnlyList<ClusterViewModel>> LoadClustersAsync();
    public Task<IReadOnlyList<ClusterViewModel>> LoadClustersForClientsAsync(IReadOnlyCollection<string> clientNames);
}
