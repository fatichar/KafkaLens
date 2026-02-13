using System.Collections.ObjectModel;
using KafkaLens.Shared;
using Serilog;

namespace KafkaLens.ViewModels;

public class ClusterFactory : IClusterFactory
{
    private readonly IClientFactory clientFactory;

    private ObservableCollection<ClusterViewModel> Clusters { get; } = new();

    public ClusterFactory(IClientFactory clientFactory)
    {
        this.clientFactory = clientFactory;
    }

    public ObservableCollection<ClusterViewModel> GetAllClusters()
    {
        return Clusters;
    }

    public async Task<ObservableCollection<ClusterViewModel>> LoadClustersAsync()
    {
        await clientFactory.LoadClientsAsync();
        var clients = clientFactory.GetAllClients();

        // call LoadClustersAsync for each client in parallel
        await Task.WhenAll(clients.Select(LoadClustersAsync));

        return Clusters;
    }

    private async Task LoadClustersAsync(IKafkaLensClient client)
    {
        try
        {
            Log.Information("Loading clusters for client: {ClientName}", client.Name);
            var clusters = (await client.GetAllClustersAsync()).ToList();
            Avalonia.Threading.Dispatcher.UIThread.Post(() => UpdateClusters(client, clusters));
        }
        catch (Exception e)
        {
            Log.Error(e, "Error loading clusters");
        }
    }

    private void UpdateClusters(IKafkaLensClient client, IEnumerable<Shared.Models.KafkaCluster> clusters)
    {
        var newClusters = new List<ClusterViewModel>();
        foreach (var cluster in clusters)
        {
            var existing = Clusters.FirstOrDefault(c => c.Id == cluster.Id && c.Client.Name == client.Name);
            if (existing != null)
            {
                existing.IsConnected = cluster.IsConnected;
            }
            else
            {
                var newVm = new ClusterViewModel(cluster, client);
                Clusters.Add(newVm);
                newClusters.Add(newVm);
            }
        }

        // Validate connection for newly discovered clusters
        if (newClusters.Count > 0)
        {
            _ = Task.WhenAll(newClusters.Select(c => c.CheckConnectionAsync()));
        }

        // Handle removals if necessary
        var toRemove = Clusters.Where(c => c.Client.Name == client.Name && !clusters.Any(newC => newC.Id == c.Id)).ToList();
        foreach (var item in toRemove)
        {
            Clusters.Remove(item);
        }
    }
}