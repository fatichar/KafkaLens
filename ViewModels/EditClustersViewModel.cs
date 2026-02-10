using System;
using System.Collections.ObjectModel;
using System.Linq;
using KafkaLens.Clients.Entities;
using KafkaLens.Shared.DataAccess;
using KafkaLens.Shared.Entities;

namespace KafkaLens.ViewModels;

public class EditClustersViewModel
{
    private IClusterInfoRepository ClusterRepository { get; }
    private IClientInfoRepository ClientRepository { get; }

    public ObservableCollection<ClusterInfo> Clusters { get; }
    public ObservableCollection<ClientInfo> Clients { get; }

    public EditClustersViewModel(
        IClusterInfoRepository clusterInfoRepository,
        IClientInfoRepository clientInfoRepository)
    {
        ClusterRepository = clusterInfoRepository;
        ClientRepository = clientInfoRepository;

        Clusters = new ObservableCollection<ClusterInfo>(ClusterRepository.GetAll().Values);
        Clients = new ObservableCollection<ClientInfo>(ClientRepository.GetAll().Values);
    }

    // Clusters
    public void AddCluster(string name, string address)
    {
        var clusterInfo = ClusterRepository.Add(name, address);
        Clusters.Add(clusterInfo);
    }

    public void UpdateCluster(ClusterInfo updated)
    {
        ClusterRepository.Update(updated);
        var existing = Clusters.FirstOrDefault(c => c.Id == updated.Id);
        if (existing != null)
        {
            var index = Clusters.IndexOf(existing);
            Clusters[index] = updated;
        }
    }

    public void RemoveCluster(ClusterInfo? clusterInfo)
    {
        if (clusterInfo == null) return;
        ClusterRepository.Delete(clusterInfo.Id);
        Clusters.Remove(clusterInfo);
    }

    // Clients
    public void AddClient(string name, string address, string protocol = "grpc")
    {
        var id = Guid.NewGuid().ToString();
        var clientInfo = new ClientInfo(id, name, address, protocol);
        ClientRepository.Add(clientInfo);
        Clients.Add(clientInfo);
    }

    public void UpdateClient(ClientInfo updated)
    {
        ClientRepository.Update(updated);
        var existing = Clients.FirstOrDefault(c => c.Id == updated.Id);
        if (existing != null)
        {
            var index = Clients.IndexOf(existing);
            Clients[index] = updated;
        }
    }

    public void RemoveClient(ClientInfo? clientInfo)
    {
        if (clientInfo == null) return;
        ClientRepository.Delete(clientInfo.Id);
        Clients.Remove(clientInfo);
    }
}
