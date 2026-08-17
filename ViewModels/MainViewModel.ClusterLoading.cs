using System.Collections.Specialized;
using System.Threading;
using Avalonia.Threading;
using KafkaLens.Shared;
using KafkaLens.Shared.Models;
using Serilog;

namespace KafkaLens.ViewModels;

public partial class MainViewModel
{
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
    }

    private async Task RefreshClustersForHealthCheckAsync()
    {
        if (!isStartupLoadCompleted) return;

        await RunSerializedClusterFlowAsync(async () =>
        {
            await RefreshClustersAsync();
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
                existing.ReplaceClient(loaded.Client, resetTopics: false);
                existing.Name = loaded.Name;
                existing.Address = loaded.Address;
                ApplyLoadedStatus(existing, loaded);
            }
            else
            {
                Clusters.Add(loaded);
                if (loaded.IsEnabled)
                {
                    _ = loaded.CheckConnectionAsync();
                }
            }
        }

        var removedClusters = new List<ClusterViewModel>();
        foreach (var (key, cluster) in existingByKey)
        {
            if (!loadedByKey.ContainsKey(key))
            {
                Clusters.Remove(cluster);
                removedClusters.Add(cluster);
            }
        }

        ReattachOrphanedTabs(removedClusters, loadedClusters);
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
                existing.ReplaceClient(loaded.Client, resetTopics: false);
                existing.Name = loaded.Name;
                existing.Address = loaded.Address;
                ApplyLoadedStatus(existing, loaded);
            }
            else
            {
                Clusters.Add(loaded);
                if (loaded.IsEnabled)
                {
                    _ = loaded.CheckConnectionAsync();
                }
            }
        }

        var removedClusters = new List<ClusterViewModel>();
        foreach (var (key, cluster) in existingByKey)
        {
            if (!loadedByKey.ContainsKey(key))
            {
                Clusters.Remove(cluster);
                removedClusters.Add(cluster);
            }
        }

        ReattachOrphanedTabs(removedClusters, loadedClusters);
    }

    /// <summary>
    /// When a cluster disappears from a client's discovered list (e.g. a placeholder created while
    /// the client was unreachable gets replaced once discovery succeeds), any already-open tabs
    /// pointing at the removed cluster would otherwise be stuck forever with a stale/invalid
    /// identity. If the client now resolves to exactly one real cluster, re-point those tabs at it
    /// so they recover without the user needing to close and reopen them.
    /// </summary>
    private void ReattachOrphanedTabs(IReadOnlyList<ClusterViewModel> removedClusters, IReadOnlyList<ClusterViewModel> loadedClusters)
    {
        if (removedClusters.Count == 0) return;

        foreach (var clientName in removedClusters.Select(c => c.Client.Name).Distinct(StringComparer.Ordinal))
        {
            var replacement = loadedClusters.Where(c => c.Client.Name == clientName).ToList();
            if (replacement.Count != 1) continue;

            var newCluster = replacement[0];
            var removedIdsForClient = removedClusters
                .Where(c => c.Client.Name == clientName)
                .Select(c => c.Id)
                .ToHashSet(StringComparer.Ordinal);

            foreach (var opened in OpenedClusters.Where(o => removedIdsForClient.Contains(o.ClusterId)))
            {
                opened.ReattachCluster(newCluster);
            }
        }
    }

    private async Task RefreshClustersAsync()
    {
        // Periodic health check: single attempt per cluster, no retries.
        await Task.WhenAll(Clusters.Where(c => c.IsEnabled).Select(c => CheckConnectionSafeAsync(c, allowRetries: false)));
    }

    private async Task CheckConnectionSafeAsync(ClusterViewModel cluster, bool allowRetries = true)
    {
        try
        {
            await cluster.CheckConnectionAsync(allowRetries);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed connection check for cluster {ClusterName}", cluster.Name);
            cluster.Status = ConnectionState.Failed;
        }
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

    /// <summary>
    /// Re-discovers the cluster list for a single client immediately, without waiting for the
    /// periodic health-check timer. Used when the user opens a cluster whose client hasn't
    /// successfully produced a real cluster list yet (e.g. it was unreachable at startup), so a
    /// stale/placeholder identity doesn't get stuck until the next scheduled refresh.
    /// </summary>
    public async Task RefreshClustersForClientAsync(string clientName)
    {
        if (!isStartupLoadCompleted) return;

        await RunSerializedClusterFlowAsync(async () =>
        {
            var discovered = await clusterFactory.LoadClustersForClientsAsync(new HashSet<string>(StringComparer.Ordinal) { clientName });
            ApplyClusterSnapshotForClients(discovered, new HashSet<string>(StringComparer.Ordinal) { clientName });
            EnsureOpenedClustersSubscriptionInitialized();
            UpdateOpenedClusters();
        });
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
                opened.UpdateClusterName(cluster.Name);
        }
    }

    private void OnClustersChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (args?.OldItems != null)
        {
            foreach (ClusterViewModel item in args.OldItems)
            {
                item.PropertyChanged -= OnClusterPropertyChanged;
                RemoveClusterFromMenu(item);
                CloseTabsForCluster(item.Id);
            }
        }

        if (args?.NewItems != null)
        {
            foreach (ClusterViewModel item in args.NewItems)
            {
                item.PropertyChanged += OnClusterPropertyChanged;
                if (item.IsEnabled)
                {
                    AddClusterToMenu(item);
                }
            }
        }
    }

    private static string GetClusterKey(ClusterViewModel cluster) => $"{cluster.Client.Name}:{cluster.Id}";

    private static void ApplyLoadedStatus(ClusterViewModel existing, ClusterViewModel loaded)
    {
        if (loaded.Status != ConnectionState.Unknown || existing.Status == ConnectionState.Unknown)
            existing.Status = loaded.Status;
    }
}
