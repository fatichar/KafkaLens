using System.Collections.ObjectModel;
using KafkaLens.Clients;
using KafkaLens.Clients.Entities;
using KafkaLens.Shared;
using Serilog;

namespace KafkaLens.ViewModels;

public class ClusterFactory : IClusterFactory
{
    private readonly IClientFactory clientFactory;

    private List<ClusterViewModel> Clusters { get; } = new();
    
    public ClusterFactory(IClientFactory clientFactory)
    {
        this.clientFactory = clientFactory;
    }
    
    public List<ClusterViewModel> GetAllClusters()
    {
        return Clusters;
    }

    public async Task LoadClustersAsync()
    {
        if (Clusters.Count > 0)
        {
            return;
        }
        await clientFactory.LoadClientsAsync();
        var clients = clientFactory.GetAllClients();

        // call LoadClusters for each client in parallel
        var tasks = clients.Select(LoadClusters).ToList();
        await Task.WhenAll(tasks);
    }

    private async Task LoadClusters(IKafkaLensClient client)
    {
        try
        {
            Log.Information("Loading clusters for client: {ClientName}", client.Name);
            var clusters = await client.GetAllClustersAsync();
            foreach (var cluster in clusters)
            {
                Log.Information("Found cluster: {ClusterName}", cluster.Name);
                Clusters.Add(new ClusterViewModel(cluster, client));
            }
        }
        catch (Exception e)
        {
            Log.Error(e, "Error loading clusters");
        }
    }
}