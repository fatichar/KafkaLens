using System.Collections.ObjectModel;
using System.Collections.Specialized;
using KafkaLens.Clients.Entities;
using KafkaLens.Shared;
using KafkaLens.Shared.DataAccess;
using KafkaLens.Shared.Entities;
using KafkaLens.Shared.Models;

namespace KafkaLens.ViewModels;

public class EditClustersViewModel : IDisposable
{
    private IClusterInfoRepository ClusterRepository { get; }
    private IClientInfoRepository ClientRepository { get; }
    private IClientFactory ClientFactory { get; }
    private Func<string, Task>? RefreshClustersForClient { get; }

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
        IClientFactory clientFactory,
        Func<string, Task>? refreshClustersForClient = null)
    {
        AllClusters = clusters;
        Clusters = new ObservableCollection<ClusterViewModel>(clusters.Where(c => c.Client.CanEditClusters));
        ClusterRepository = clusterInfoRepository;
        ClientRepository = clientInfoRepository;
        ClientFactory = clientFactory;
        RefreshClustersForClient = refreshClustersForClient;

        AllClusters.CollectionChanged += AllClusters_CollectionChanged;

        Clients = new ObservableCollection<ClientInfoViewModel>(ClientRepository.GetAll().Values.Select(c => new ClientInfoViewModel(c)));

        CheckClientConnectionsAsync();
    }

    private void AllClusters_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (ClusterViewModel item in e.NewItems)
            {
                if (item.Client.CanEditClusters && !Clusters.Contains(item))
                {
                    Clusters.Add(item);
                }
            }
        }
        if (e.OldItems != null)
        {
            foreach (ClusterViewModel item in e.OldItems)
            {
                Clusters.Remove(item);
            }
        }
    }

    public void Dispose()
    {
        AllClusters.CollectionChanged -= AllClusters_CollectionChanged;
    }

    private async void CheckClientConnectionsAsync()
    {
        var tasks = Clients.Select(CheckClientConnectionAsync).ToList();
        await Task.WhenAll(tasks);
    }

    private async Task CheckClientConnectionAsync(ClientInfoViewModel client)
    {
        try
        {
            client.Status = ConnectionState.Checking;
            var kafkaClient = ClientFactory.GetClient(client.Name);
            var clusters = (await kafkaClient.GetAllClustersAsync()).ToList();
            // If GrpcClient fails to connect, it returns a single cluster with IsConnected=false
            if (clusters.Count == 1 && clusters.First().Status == ConnectionState.Failed)
            {
                 client.Status = ConnectionState.Failed;
            }
            else
            {
                client.Status = ConnectionState.Connected;
            }
        }
        catch (Exception e)
        {
            client.LastError = e.Message;
            client.Status = ConnectionState.Failed;
        }
    }

    public async Task<bool> ValidateConnectionAsync(string address)
    {
        return await LocalClient.ValidateConnectionAsync(address);
    }

    public async Task<ConnectionValidationResult> TestConnectionAsync(string address)
    {
        if (LocalClient is IConnectionTestClient diagnosticClient)
        {
            return await diagnosticClient.ValidateConnectionWithDetailsAsync(address);
        }

        return await LocalClient.ValidateConnectionAsync(address)
            ? ConnectionValidationResult.Success()
            : ConnectionValidationResult.Failed("Connection validation returned false.");
    }

    // Clusters
    public async Task AddClusterAsync(string name, string address)
    {
        var clusterInfo = ClusterRepository.Add(name, address);
        var cluster = await LocalClient.GetClusterByIdAsync(clusterInfo.Id);
        var vm = new ClusterViewModel(cluster, LocalClient);
        AllClusters.Add(vm);
        await vm.CheckConnectionAsync();
    }

    public async Task UpdateClusterAsync(ClusterViewModel cluster, string name, string address)
    {
        var addressChanged = !string.Equals(cluster.Address, address, StringComparison.Ordinal);
        var updated = new ClusterInfo(cluster.Id, name, address);

        if (addressChanged)
        {
            cluster.Status = ConnectionState.Checking;
            await LocalClient.UpdateClusterAsync(
                cluster.Id,
                new KafkaClusterUpdate(name, address));
        }
        else
        {
            ClusterRepository.Update(updated);
        }

        cluster.Name = name;
        cluster.Address = address;

        if (addressChanged)
        {
            await cluster.RecheckConnectionAsync();
        }
    }

    public void RemoveCluster(ClusterViewModel? cluster)
    {
        if (cluster == null) return;
        ClusterRepository.Delete(cluster.Id);
        AllClusters.Remove(cluster);
        Clusters.Remove(cluster);
    }

    // Clients
    public async Task AddClientAsync(string name, string address, string protocol = "grpc")
    {
        var id = Guid.NewGuid().ToString();
        var clientInfo = new ClientInfo(id, name, address, protocol);
        ClientRepository.Add(clientInfo);
        var vm = new ClientInfoViewModel(clientInfo);
        Clients.Add(vm);

        // Register the new client in the factory before trying to use it
        await ClientFactory.LoadClientsAsync();

        await CheckClientConnectionAsync(vm);

        // Load clusters from the newly added client
        await LoadClustersForClientAsync(name);
    }

    public async Task UpdateClientAsync(ClientInfo updated)
    {
        var existing = Clients.FirstOrDefault(c => c.Id == updated.Id);
        if (existing == null) return;

        var oldName = existing.Name;
        var addressChanged = !string.Equals(existing.Info.Address, updated.Address, StringComparison.Ordinal);
        var protocolChanged = !string.Equals(existing.Info.Protocol, updated.Protocol, StringComparison.Ordinal);
        var nameChanged = !string.Equals(existing.Info.Name, updated.Name, StringComparison.Ordinal);

        ClientRepository.Update(updated);
        existing.UpdateInfo(updated);
        var oldClusters = AllClusters.Where(c => c.Client.Name == oldName).ToList();
        var transportChanged = addressChanged || protocolChanged;

        if (!addressChanged && !protocolChanged && !nameChanged)
        {
            return;
        }

        if (transportChanged)
        {
            existing.Status = ConnectionState.Checking;
            foreach (var cluster in oldClusters)
            {
                cluster.Status = ConnectionState.Checking;
            }
        }

        await ClientFactory.LoadClientsAsync();
        var refreshedClient = ClientFactory.GetClient(updated.Name);

        foreach (var cluster in oldClusters)
        {
            cluster.ReplaceClient(refreshedClient, resetTopics: transportChanged);
        }

        if (transportChanged)
        {
            await CheckClientConnectionAsync(existing);

            if (RefreshClustersForClient != null)
            {
                await RefreshClustersForClient(updated.Name);
            }
            else
            {
                await LoadClustersForClientAsync(updated.Name);
            }

            var refreshedClusters = AllClusters
                .Where(c => c.Client.Name == updated.Name)
                .ToList();
            await Task.WhenAll(refreshedClusters.Select(c => c.RecheckConnectionAsync()));
        }
    }

    private async Task LoadClustersForClientAsync(string clientName)
    {
        try
        {
            var client = ClientFactory.GetClient(clientName);
            var clusters = (await client.GetAllClustersAsync()).ToList();

            var tasks = new List<Task>();
            foreach (var cluster in clusters)
            {
                var existing = AllClusters.FirstOrDefault(c => c.Id == cluster.Id && c.Client.Name == client.Name);
                if (existing == null)
                {
                    var newVm = new ClusterViewModel(cluster, client);
                    AllClusters.Add(newVm);
                    tasks.Add(newVm.CheckConnectionAsync());
                }
            }
            await Task.WhenAll(tasks);
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