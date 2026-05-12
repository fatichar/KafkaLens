using System.Collections.Specialized;
using System.Threading;
using Avalonia.Threading;
using KafkaLens.Shared;
using KafkaLens.Shared.Models;
using Serilog;

namespace KafkaLens.ViewModels;

public partial class MainViewModel
{
    private static readonly TimeSpan[] StartupHealthCheckDelays =
    [
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15)
    ];

    private bool isStartupLoadCompleted;
    private bool isOpenedClustersSubscriptionInitialized;
    private readonly SemaphoreSlim clusterRefreshLock = new(1, 1);

    private async Task LoadClustersOnStartupAsync()
    {
        IsLoadingClusters = true;
        AppLogService.LogInfo("Loading clusters and clients", "Startup");
        try
        {
            await ClientFactory.LoadClientsAsync();
            var clients = ClientFactory.GetAllClients();
            AppLogService.LogInfo($"Loaded {clients.Count} clients", "Startup");

            var loadTasks = clients.Select(async client =>
            {
                var loaded = await clusterFactory.LoadClustersForClientAsync(client);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ApplyClusterSnapshotForClients(loaded, new HashSet<string> { client.Name });
                    EnsureOpenedClustersSubscriptionInitialized();
                    UpdateOpenedClusters();
                });
            }).ToList();

            isStartupLoadCompleted = true;

            await Task.WhenAll(loadTasks);
            AppLogService.LogInfo($"Loaded {Clusters.Count} clusters", "Startup");
        }
        finally
        {
            IsLoadingClusters = false;
        }

        await TryRestoreTabsAsync();
        ScheduleStartupHealthChecks();
    }

    private async Task RefreshClustersForHealthCheckAsync()
    {
        if (!isStartupLoadCompleted) return;

        await RunSerializedClusterFlowAsync(async () =>
        {
            await RefreshDisconnectedClustersAsync();
            await DiscoverClientsNeedingRefreshAsync();
            EnsureOpenedClustersSubscriptionInitialized();
            UpdateOpenedClusters();
        });
    }

    private async Task RunSerializedClusterFlowAsync(Func<Task> action)
    {
        await clusterRefreshLock.WaitAsync();
        try
        {
            await action();
        }
        finally
        {
            clusterRefreshLock.Release();
        }
    }

    private void EnsureOpenedClustersSubscriptionInitialized()
    {
        if (isOpenedClustersSubscriptionInitialized) return;
        OpenedClusters.CollectionChanged += OnOpenedClustersChanged;
        isOpenedClustersSubscriptionInitialized = true;
    }

    private void ApplyClusterSnapshot(IReadOnlyList<ClusterViewModel> loadedClusters)
    {
        var existingByKey = Clusters.ToDictionary(GetClusterKey);
        var loadedByKey = loadedClusters.ToDictionary(GetClusterKey);
        foreach (var loaded in loadedClusters)
        {
            var key = GetClusterKey(loaded);
            if (existingByKey.TryGetValue(key, out var existing))
            {
                existing.Name = loaded.Name;
                existing.Address = loaded.Address;
                ApplyLoadedStatus(existing, loaded);
            }
            else
            {
                Clusters.Add(loaded);
                _ = loaded.CheckConnectionAsync();
            }
        }

        foreach (var (key, cluster) in existingByKey)
        {
            if (!loadedByKey.ContainsKey(key))
            {
                Clusters.Remove(cluster);
            }
        }
    }

    private void ApplyClusterSnapshotForClients(IReadOnlyList<ClusterViewModel> loadedClusters, ISet<string> clientNames)
    {
        var existingForClients = Clusters.Where(c => clientNames.Contains(c.Client.Name)).ToList();
        var existingByKey = existingForClients.ToDictionary(GetClusterKey);
        var loadedByKey = loadedClusters.ToDictionary(GetClusterKey);
        foreach (var loaded in loadedClusters)
        {
            var key = GetClusterKey(loaded);
            if (existingByKey.TryGetValue(key, out var existing))
            {
                existing.Name = loaded.Name;
                existing.Address = loaded.Address;
                ApplyLoadedStatus(existing, loaded);
            }
            else
            {
                Clusters.Add(loaded);
                _ = loaded.CheckConnectionAsync();
            }
        }

        foreach (var (key, cluster) in existingByKey)
        {
            if (!loadedByKey.ContainsKey(key))
            {
                Clusters.Remove(cluster);
            }
        }
    }

    private async Task RefreshDisconnectedClustersAsync()
    {
        var disconnected = Clusters.Where(c => c.Status != ConnectionState.Connected).ToList();
        await Task.WhenAll(disconnected.Select(CheckConnectionSafeAsync));
    }

    private async Task CheckConnectionSafeAsync(ClusterViewModel cluster)
    {
        try
        {
            await cluster.CheckConnectionAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed connection check for cluster {ClusterName}", cluster.Name);
            cluster.Status = ConnectionState.Failed;
        }
    }

    private void ScheduleStartupHealthChecks()
    {
        foreach (var delay in StartupHealthCheckDelays)
            _ = RunDelayedHealthCheckAsync(delay);
    }

    private async Task RunDelayedHealthCheckAsync(TimeSpan delay)
    {
        await Task.Delay(delay);
        await RefreshClustersForHealthCheckAsync();
    }

    private async Task DiscoverClientsNeedingRefreshAsync()
    {
        await ClientFactory.LoadClientsAsync();
        var clients = ClientFactory.GetAllClients();
        if (clients.Count == 0) return;

        var clientNamesNeedingRefresh = GetClientsNeedingDiscovery(clients);
        if (clientNamesNeedingRefresh.Count == 0) return;

        var discovered = await clusterFactory.LoadClustersForClientsAsync(clientNamesNeedingRefresh);
        ApplyClusterSnapshotForClients(discovered, clientNamesNeedingRefresh);
    }

    private HashSet<string> GetClientsNeedingDiscovery(IReadOnlyList<IKafkaLensClient> clients)
    {
        var byClient = Clusters
            .GroupBy(c => c.Client.Name)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var client in clients)
        {
            if (!byClient.TryGetValue(client.Name, out var clientClusters) || clientClusters.Count == 0 ||
                clientClusters.Any(c => c.Status != ConnectionState.Connected))
            {
                result.Add(client.Name);
            }
        }

        return result;
    }

    private void UpdateOpenedClusters()
    {
        foreach (var opened in OpenedClusters)
        {
            var cluster = Clusters.FirstOrDefault(c => c.Id == opened.ClusterId);
            if (cluster != null)
                opened.Name = cluster.Name;
        }
    }

    private void OnClustersChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args?.OldItems != null)
            foreach (ClusterViewModel item in args.OldItems)
                openClusterMenuItems.Remove(openClusterMenuItems.First(x => x.Header == item.Name));

        if (args?.NewItems != null)
            foreach (ClusterViewModel item in args.NewItems)
                AddClusterToMenu(item);
    }

    private static string GetClusterKey(ClusterViewModel cluster) => $"{cluster.Client.Name}:{cluster.Id}";

    private static void ApplyLoadedStatus(ClusterViewModel existing, ClusterViewModel loaded)
    {
        if (loaded.Status != ConnectionState.Unknown || existing.Status == ConnectionState.Unknown)
            existing.Status = loaded.Status;
    }
}
