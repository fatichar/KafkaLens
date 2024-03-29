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

    public void Add(string name, string address)
    {
        var clusterInfo = Repository.Add(name, address);
        Clusters.Add(clusterInfo);
    }

    public void Remove(ClusterInfo? clusterInfo)
    {
        if (clusterInfo is null)
        {
            return;
        }
        Repository.Delete(clusterInfo.Id);
        Clusters.Remove(clusterInfo);
    }
}