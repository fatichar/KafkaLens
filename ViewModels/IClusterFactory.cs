using System.Collections.ObjectModel;

namespace KafkaLens.ViewModels;

public interface IClusterFactory
{
    public Task<ObservableCollection<ClusterViewModel>> LoadClustersAsync();
    public ObservableCollection<ClusterViewModel> GetAllClusters();
}