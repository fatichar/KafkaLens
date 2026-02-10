using System;
using System.Collections.ObjectModel;
using System.Linq;
using KafkaLens.Clients.Entities;
using KafkaLens.Shared;
using KafkaLens.Shared.DataAccess;
using KafkaLens.Shared.Entities;

namespace KafkaLens.ViewModels;

public class EditClustersViewModel
{
    private IClusterInfoRepository ClusterRepository { get; }
    private IClientInfoRepository ClientRepository { get; }
    private IClientFactory ClientFactory { get; }

    public ObservableCollection<ClusterInfoViewModel> Clusters { get; }
    public ObservableCollection<ClientInfoViewModel> Clients { get; }

    public EditClustersViewModel(
        IClusterInfoRepository clusterInfoRepository,
        IClientInfoRepository clientInfoRepository,
        IClientFactory clientFactory)
    {
        ClusterRepository = clusterInfoRepository;
        ClientRepository = clientInfoRepository;
        ClientFactory = clientFactory;

        Clusters = new ObservableCollection<ClusterInfoViewModel>(ClusterRepository.GetAll().Values.Select(c => new ClusterInfoViewModel(c)));
        Clients = new ObservableCollection<ClientInfoViewModel>(ClientRepository.GetAll().Values.Select(c => new ClientInfoViewModel(c)));

        CheckConnectionsAsync();
    }

    private async void CheckConnectionsAsync()
    {
        var localClient = ClientFactory.GetClient("Local");
        foreach (var cluster in Clusters)
        {
            _ = CheckClusterConnectionAsync(localClient, cluster);
        }
        foreach (var client in Clients)
        {
            _ = CheckClientConnectionAsync(client);
        }
    }

    private async Task CheckClusterConnectionAsync(IKafkaLensClient localClient, ClusterInfoViewModel cluster)
    {
        cluster.IsConnected = await localClient.ValidateConnectionAsync(cluster.Address);
    }

    private async Task CheckClientConnectionAsync(ClientInfoViewModel client)
    {
        try
        {
            var kafkaClient = ClientFactory.GetClient(client.Name);
            var clusters = await kafkaClient.GetAllClustersAsync();
            // If GrpcClient fails to connect, it returns a single cluster with IsConnected=false
            if (clusters.Count() == 1 && !clusters.First().IsConnected)
            {
                 client.IsConnected = false;
            }
            else
            {
                client.IsConnected = true;
            }
        }
        catch (Exception)
        {
            client.IsConnected = false;
        }
    }

    public async Task<bool> ValidateConnectionAsync(string address)
    {
        var localClient = ClientFactory.GetClient("Local");
        return await localClient.ValidateConnectionAsync(address);
    }

    // Clusters
    public void AddCluster(string name, string address)
    {
        var clusterInfo = ClusterRepository.Add(name, address);
        var vm = new ClusterInfoViewModel(clusterInfo);
        Clusters.Add(vm);
        var localClient = ClientFactory.GetClient("Local");
        _ = CheckClusterConnectionAsync(localClient, vm);
    }

    public void UpdateCluster(ClusterInfo updated)
    {
        ClusterRepository.Update(updated);
        var existing = Clusters.FirstOrDefault(c => c.Id == updated.Id);
        if (existing != null)
        {
            var index = Clusters.IndexOf(existing);
            var vm = new ClusterInfoViewModel(updated);
            Clusters[index] = vm;
            var localClient = ClientFactory.GetClient("Local");
            _ = CheckClusterConnectionAsync(localClient, vm);
        }
    }

    public void RemoveCluster(ClusterInfoViewModel? clusterInfo)
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
        var vm = new ClientInfoViewModel(clientInfo);
        Clients.Add(vm);
        _ = CheckClientConnectionAsync(vm);
    }

    public void UpdateClient(ClientInfo updated)
    {
        ClientRepository.Update(updated);
        var existing = Clients.FirstOrDefault(c => c.Id == updated.Id);
        if (existing != null)
        {
            var index = Clients.IndexOf(existing);
            var vm = new ClientInfoViewModel(updated);
            Clients[index] = vm;
            _ = CheckClientConnectionAsync(vm);
        }
    }

    public void RemoveClient(ClientInfoViewModel? clientInfo)
    {
        if (clientInfo == null) return;
        ClientRepository.Delete(clientInfo.Id);
        Clients.Remove(clientInfo);
    }
}
