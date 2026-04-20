using KafkaLens.Shared;
using KafkaLens.ViewModels.Services;
using Serilog;

namespace KafkaLens.ViewModels;

public class ClusterFactory(IClientFactory clientFactory, IAppLogService? appLogService = null) : IClusterFactory
{
    public async Task<IReadOnlyList<ClusterViewModel>> LoadClustersAsync()
    {
        await clientFactory.LoadClientsAsync();

        var clients = clientFactory.GetAllClients();
        return await LoadForClientsAsync(clients);
    }

    public async Task<IReadOnlyList<ClusterViewModel>> LoadClustersForClientsAsync(IReadOnlyCollection<string> clientNames)
    {
        if (clientNames.Count == 0)
        {
            return Array.Empty<ClusterViewModel>();
        }

        await clientFactory.LoadClientsAsync();
        var clientNameSet = clientNames.ToHashSet(StringComparer.Ordinal);
        var clients = clientFactory.GetAllClients()
            .Where(client => clientNameSet.Contains(client.Name))
            .ToList();

        return await LoadForClientsAsync(clients);
    }

    public async Task<IReadOnlyList<ClusterViewModel>> LoadClustersForClientAsync(IKafkaLensClient client)
    {
        return await LoadForClientAsync(client);
    }

    private static IReadOnlyList<ClusterViewModel> SortClusters(IReadOnlyCollection<ClusterViewModel> clusters)
    {
        return clusters
            .OrderBy(c => c.Client.Name, StringComparer.Ordinal)
            .ThenBy(c => c.Name, StringComparer.Ordinal)
            .ToList();
    }

    private async Task<IReadOnlyList<ClusterViewModel>> LoadForClientsAsync(IReadOnlyCollection<IKafkaLensClient> clients)
    {
        var tasks = clients.Select(LoadForClientAsync);
        var clustersByClient = await Task.WhenAll(tasks);
        var flattened = clustersByClient.SelectMany(c => c).ToList();
        return SortClusters(flattened);
    }

    private async Task<IReadOnlyList<ClusterViewModel>> LoadForClientAsync(IKafkaLensClient client)
    {
        try
        {
            Log.Information("Loading clusters for client: {ClientName}", client.Name);
            appLogService?.LogInfo($"Loading clusters from {client.Name}", "Startup");
            var clusters = await client.GetAllClustersAsync();
            var result = clusters.Select(cluster => new ClusterViewModel(cluster, client, appLogService)).ToList();
            appLogService?.LogInfo($"Loaded {result.Count} clusters from {client.Name}", "Startup");
            return result;
        }
        catch (Exception e)
        {
            Log.Error(e, "Error loading clusters for client: {ClientName}", client.Name);
            appLogService?.LogError($"Could not load clusters from {client.Name}: {e.Message}", "Startup");
            return Array.Empty<ClusterViewModel>();
        }
    }
}
