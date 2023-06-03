using System.Collections.ObjectModel;
using KafkaLens.Shared.DataAccess;
using KafkaLens.Shared.Entities;

namespace KafkaLens.ViewModels;

public class LocalClustersViewModel
{
    private IClusterInfoRepository Repository { get; }
    public ObservableCollection<ClusterInfo> Clusters { get; }
    
    public LocalClustersViewModel(IClusterInfoRepository clusterInfoRepository)
    {
        Repository = clusterInfoRepository;
        Clusters = new ObservableCollection<ClusterInfo>(Repository.GetAll().Values);
    }

    public void Save()
    {
        Repository.DeleteAll();
        Repository.AddAll(Clusters);
    }

    public void Add(ClusterInfo clusterInfo)
    {
        Repository.Add(clusterInfo);
        Clusters.Add(clusterInfo);
    }

    public void Remove(ClusterInfo? clusterInfo)
    {
        Clusters.Remove(clusterInfo);
    }
}