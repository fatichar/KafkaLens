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

    private ObservableCollection<ClusterViewModel> AllClusters { get; }
    public ObservableCollection<ClusterViewModel> Clusters { get; }
    public ObservableCollection<ClientInfoViewModel> Clients { get; }

    private IKafkaLensClient LocalClient
    {
        get
        {
            field ??= ClientFactory.GetClient("Local");
            return field;
        }
    }

    public EditClustersViewModel(
        ObservableCollection<ClusterViewModel> clusters,
        IClusterInfoRepository clusterInfoRepository,
        IClientInfoRepository clientInfoRepository,
        IClientFactory clientFactory)
    {
        AllClusters = clusters;
        Clusters = new ObservableCollection<ClusterViewModel>(clusters.Where(c => c.Client.CanEditClusters));
        ClusterRepository = clusterInfoRepository;
        ClientRepository = clientInfoRepository;
        ClientFactory = clientFactory;

        Clients = new ObservableCollection<ClientInfoViewModel>(ClientRepository.GetAll().Values.Select(c => new ClientInfoViewModel(c)));

        CheckClientConnectionsAsync();
    }

    private async void CheckClientConnectionsAsync()
    {
        foreach (var client in Clients)
        {
            _ = CheckClientConnectionAsync(client);
        }
    }

    private async Task CheckClientConnectionAsync(ClientInfoViewModel client)
    {
        try
        {
            var kafkaClient = ClientFactory.GetClient(client.Name);
            var clusters = (await kafkaClient.GetAllClustersAsync()).ToList();
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
        return await LocalClient.ValidateConnectionAsync(address);
    }

    // Clusters
    public async Task AddClusterAsync(string name, string address)
    {
        var clusterInfo = ClusterRepository.Add(name, address);
        var cluster = await LocalClient.GetClusterByIdAsync(clusterInfo.Id);
        var vm = new ClusterViewModel(cluster, LocalClient);
        AllClusters.Add(vm);
        Clusters.Add(vm);
        _ = vm.CheckConnectionAsync();
    }

    public async Task UpdateClusterAsync(ClusterViewModel cluster, string name, string address)
    {
        var updated = new ClusterInfo(cluster.Id, name, address);
        ClusterRepository.Update(updated);
        // Cluster will be refreshed on next LoadClusters call
        _ = cluster.CheckConnectionAsync();
    }

    public void RemoveCluster(ClusterViewModel? cluster)
    {
        if (cluster == null) return;
        ClusterRepository.Delete(cluster.Id);
        AllClusters.Remove(cluster);
        Clusters.Remove(cluster);
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