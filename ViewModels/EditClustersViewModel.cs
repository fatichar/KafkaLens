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
            var succeeded = IsClientConnectionSuccessful(clusters);
            ApplyClientConnectionResult(client.Name, clusters, succeeded);
            client.LastError = succeeded ? null : GetClientConnectionError(clusters);
            client.Status = succeeded ? ConnectionState.Connected : ConnectionState.Failed;
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

    public async Task<ConnectionValidationResult> TestConnectionAsync(ClusterViewModel cluster, string address)
    {
        var isCurrentAddress = string.Equals(
            cluster.Address.Trim(),
            address.Trim(),
            StringComparison.Ordinal);

        if (!isCurrentAddress)
        {
            return await TestConnectionAsync(address);
        }

        cluster.Status = ConnectionState.Checking;
        try
        {
            var result = await TestConnectionAsync(address);
            cluster.LastError = result.Succeeded ? null : result.ErrorMessage;
            cluster.Status = result.Succeeded ? ConnectionState.Connected : ConnectionState.Failed;
            return result;
        }
        catch (Exception e)
        {
            cluster.LastError = e.Message;
            cluster.Status = ConnectionState.Failed;
            throw;
        }
    }

    public Task<ConnectionValidationResult> TestClientConnectionAsync(string address, string protocol = "grpc")
    {
        var clientInfo = new ClientInfo(Guid.NewGuid().ToString(), "Connection test", address.Trim(), protocol);
        return TestClientConnectionCoreAsync(clientInfo);
    }

    public async Task<ConnectionValidationResult> TestClientConnectionAsync(ClientInfoViewModel client, string address)
    {
        var isCurrentAddress = string.Equals(
            client.Address.Trim(),
            address.Trim(),
            StringComparison.Ordinal);
        if (!isCurrentAddress)
        {
            return await TestClientConnectionAsync(address, client.Protocol);
        }

        client.Status = ConnectionState.Checking;
        foreach (var cluster in AllClusters.Where(c => c.Client.Name == client.Name))
        {
            cluster.Status = ConnectionState.Checking;
        }

        try
        {
            var result = await TestClientConnectionCoreAsync(
                new ClientInfo(client.Id, client.Name, address.Trim(), client.Protocol));
            client.LastError = result.Succeeded ? null : result.ErrorMessage;
            client.Status = result.Succeeded ? ConnectionState.Connected : ConnectionState.Failed;
            return result;
        }
        catch (Exception e)
        {
            client.LastError = e.Message;
            client.Status = ConnectionState.Failed;
            foreach (var cluster in AllClusters.Where(c => c.Client.Name == client.Name))
            {
                cluster.LastError = e.Message;
                cluster.Status = ConnectionState.Failed;
            }
            throw;
        }
    }

    private async Task<ConnectionValidationResult> TestClientConnectionCoreAsync(ClientInfo clientInfo)
    {
        var clusters = (await ClientFactory.TestConnectionAsync(clientInfo)).ToList();
        var failedCluster = clusters.FirstOrDefault(c => c.Status == ConnectionState.Failed);
        var succeeded = IsClientConnectionSuccessful(clusters);

        if (clientInfo.Name != "Connection test")
        {
            ApplyClientConnectionResult(clientInfo.Name, clusters, succeeded);
        }

        return succeeded
            ? ConnectionValidationResult.Success()
            : ConnectionValidationResult.Failed(
                failedCluster?.LastError ?? "The KafkaLens client or one of its clusters is unavailable.");
    }

    private static bool IsClientConnectionSuccessful(IReadOnlyList<KafkaCluster> clusters)
    {
        return !clusters.Any(c => c.Status == ConnectionState.Failed) &&
               !clusters.Any(c => c.Id.StartsWith("grpc-unavailable:", StringComparison.Ordinal) ||
                                  c.Id.StartsWith("client-unavailable:", StringComparison.Ordinal));
    }

    private static string GetClientConnectionError(IReadOnlyList<KafkaCluster> clusters)
    {
        return clusters.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.LastError))?.LastError
               ?? "The KafkaLens client or one of its clusters is unavailable.";
    }

    private void ApplyClientConnectionResult(
        string clientName,
        IReadOnlyList<KafkaCluster> results,
        bool clientSucceeded)
    {
        var clusters = AllClusters.Where(c => c.Client.Name == clientName).ToList();
        var resultById = results
            .Where(c => !c.Id.StartsWith("grpc-unavailable:", StringComparison.Ordinal) &&
                        !c.Id.StartsWith("client-unavailable:", StringComparison.Ordinal))
            .ToDictionary(c => c.Id, StringComparer.Ordinal);

        foreach (var cluster in clusters)
        {
            if (resultById.TryGetValue(cluster.Id, out var result))
            {
                cluster.Name = result.Name;
                cluster.Address = result.Address;
                cluster.LastError = result.LastError;
                cluster.Status = result.Status;
            }
            else
            {
                cluster.LastError = clientSucceeded ? "Cluster was not returned by the KafkaLens client." : "KafkaLens client is unavailable.";
                cluster.Status = ConnectionState.Failed;
            }
        }
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