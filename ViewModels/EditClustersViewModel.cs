using System.Collections.ObjectModel;
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
    public async void AddClient(string name, string address, string protocol = "grpc")
    {
        var id = Guid.NewGuid().ToString();
        var clientInfo = new ClientInfo(id, name, address, protocol);
        ClientRepository.Add(clientInfo);
        var vm = new ClientInfoViewModel(clientInfo);
        Clients.Add(vm);
        _ = CheckClientConnectionAsync(vm);
        
        // Load clusters from the newly added client
        await LoadClustersForClientAsync(name);
    }

    public async void UpdateClient(ClientInfo updated)
    {
        ClientRepository.Update(updated);
        var existing = Clients.FirstOrDefault(c => c.Id == updated.Id);
        if (existing != null)
        {
            // Check if anything actually changed that would require cluster reload
            var hasChanges = existing.Info.Name != updated.Name || 
                           existing.Info.Address != updated.Address || 
                           existing.Info.Protocol != updated.Protocol;
            
            var index = Clients.IndexOf(existing);
            var vm = new ClientInfoViewModel(updated);
            Clients[index] = vm;
            _ = CheckClientConnectionAsync(vm);
            
            // Only reload clusters if there are actual changes
            if (hasChanges)
            {
                // Remove old clusters and reload from updated client
                var oldClusters = AllClusters.Where(c => c.Client.Name == updated.Name).ToList();
                foreach (var cluster in oldClusters)
                {
                    AllClusters.Remove(cluster);
                    Clusters.Remove(cluster);
                }
                
                await LoadClustersForClientAsync(updated.Name);
            }
        }
    }

    private async Task LoadClustersForClientAsync(string clientName)
    {
        try
        {
            var client = ClientFactory.GetClient(clientName);
            var clusters = (await client.GetAllClustersAsync()).ToList();
            
            foreach (var cluster in clusters)
            {
                var existing = AllClusters.FirstOrDefault(c => c.Id == cluster.Id && c.Client.Name == client.Name);
                if (existing == null)
                {
                    var newVm = new ClusterViewModel(cluster, client);
                    AllClusters.Add(newVm);
                    if (client.CanEditClusters)
                    {
                        Clusters.Add(newVm);
                    }
                    _ = newVm.CheckConnectionAsync();
                }
            }
        }
        catch (Exception)
        {
            // If we can't load clusters, just continue - the connection check will handle the error state
        }
    }

    public void RemoveClient(ClientInfoViewModel? clientInfo)
    {
        if (clientInfo == null) return;
        
        // Remove all clusters belonging to this client
        var clustersToRemove = AllClusters.Where(c => c.Client.Name == clientInfo.Name).ToList();
        foreach (var cluster in clustersToRemove)
        {
            AllClusters.Remove(cluster);
            Clusters.Remove(cluster);
        }
        
        ClientRepository.Delete(clientInfo.Id);
        Clients.Remove(clientInfo);
    }
}