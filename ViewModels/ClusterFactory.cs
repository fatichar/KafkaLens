using System.Collections.ObjectModel;
using KafkaLens.Clients;
using KafkaLens.Clients.Entities;
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
        if (Clusters.Count > 0)
        {
            return Clusters;
        }
        await clientFactory.LoadClientsAsync();
        var clients = clientFactory.GetAllClients();

        // call LoadClustersAsync for each client in parallel
        clients.ForEach(client => LoadClustersAsync(client).ConfigureAwait(false));
        
        return Clusters;
    }

    private async Task LoadClustersAsync(IKafkaLensClient client)
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