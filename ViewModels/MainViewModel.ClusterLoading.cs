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
    private readonly HashSet<ClusterViewModel> subscribedClusters = new();
    private bool isReconcilingClusters;

    private async Task LoadClustersOnStartupAsync()
    {
        IsLoadingClusters = true;
        AppLogService.LogInfo("Loading clusters and clients", "Startup");
        try
        {
            await ClientFactory.LoadClientsAsync();
            RefreshClientEnabledIndex();
            var clients = ClientFactory.GetAllClients().Where(c =>
                (c.Name == "Local" && c.CanEditClusters) || IsClientEnabled(c.Name)).ToList();
            AppLogService.LogInfo($"Loaded {clients.Count} clients", "Startup");
            var loadTasks = clients.Select(async client =>
            {
                var loaded = await clusterFactory.LoadClustersForClientAsync(client);
                var checks = await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var pending = ApplyClusterSnapshotForClients(loaded, new HashSet<string> { client.Name });
                    EnsureOpenedClustersSubscriptionInitialized();
                    UpdateOpenedClusters();
                    return pending;
                });
                await Task.WhenAll(checks.Select(c => CheckConnectionSafeAsync(c, periodic: false)));
            }).ToList();
            await Task.WhenAll(loadTasks);
            isStartupLoadCompleted = true;
            AppLogService.LogInfo($"Loaded {Clusters.Count} clusters", "Startup");
        }
        finally
        {
            IsLoadingClusters = false;
        }
        await TryRestoreTabsAsync();
    }

    /// <summary>Internal for deterministic test invocation; production code only reaches this via the periodic timer.</summary>
    internal async Task RefreshClustersForHealthCheckAsync(CancellationToken cancellationToken = default)
    {
        if (!isStartupLoadCompleted) return;
        if (!await clusterRefreshLock.WaitAsync(0, cancellationToken))
        {
            // Another cluster flow (e.g. a slow client refresh) holds the lock. Skipping keeps
            // health checks from queueing up, but log it so a stuck flow is diagnosable rather
            // than silently freezing every subsequent status update.
            Log.Debug("Skipping periodic health check: another cluster refresh is in progress");
            return;
        }
        try
        {
            await Dispatcher.UIThread.InvokeAsync(RefreshAvailability);
            await RefreshClustersAsync(cancellationToken);
            await DiscoverClientsNeedingRefreshAsync(cancellationToken);
        }
        finally
        {
            clusterRefreshLock.Release();
        }
    }

    private async Task ObserveClusterFlowAsync(Task operation)
    {
        try { await operation; }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Log.Error(e, "Cluster refresh failed");
            AppLogService.LogError($"Cluster refresh failed: {e.Message}", "Connection");
        }
    }

    private void EnsureOpenedClustersSubscriptionInitialized()
    {
        if (isOpenedClustersSubscriptionInitialized) return;
        OpenedClusters.CollectionChanged += OnOpenedClustersChanged;
        isOpenedClustersSubscriptionInitialized = true;
    }

    private List<ClusterViewModel> ApplyClusterSnapshotForClients(IReadOnlyList<ClusterViewModel> loadedClusters, ISet<string> clientNames)
    {
        var existingByKey = Clusters.Where(c => clientNames.Contains(c.Client.Name)).ToDictionary(GetClusterKey);
        var loadedByKey = loadedClusters.ToDictionary(GetClusterKey);
        var checks = new List<ClusterViewModel>();
        var canonical = new List<ClusterViewModel>();
        var removedClusters = new List<ClusterViewModel>();
        isReconcilingClusters = true;
        try
        {
            foreach (var loaded in loadedClusters)
            {
                var key = GetClusterKey(loaded);
                if (existingByKey.TryGetValue(key, out var existing))
                {
                    var wasAvailable = existing.IsAvailable;
                    var addressChanged = existing.Address != loaded.Address || existing.IsUnavailablePlaceholder != loaded.IsUnavailablePlaceholder;
                    existing.UpdatePlaceholder(loaded.IsUnavailablePlaceholder);
                    existing.ReplaceClient(loaded.Client, resetTopics: addressChanged);
                    existing.Name = loaded.Name;
                    existing.Address = loaded.Address;
                    existing.IsEnabled = loaded.IsEnabled;
                    ApplyClientAvailability(existing);
                    ApplyLoadedStatus(existing, loaded);
                    if (!existing.IsAvailable) CloseTabsForCluster(existing.Id);
                    if (existing.IsAvailable && !existing.IsUnavailablePlaceholder && (addressChanged || !wasAvailable))
                        checks.Add(existing);
                    canonical.Add(existing);
                }
                else
                {
                    ApplyClientAvailability(loaded);
                    Clusters.Add(loaded);
                    canonical.Add(loaded);
                    if (loaded.IsAvailable && !loaded.IsUnavailablePlaceholder && loaded.Status != ConnectionState.Connected)
                        checks.Add(loaded);
                }
            }
            foreach (var (key, cluster) in existingByKey)
            {
                if (loadedByKey.ContainsKey(key)) continue;
                Clusters.Remove(cluster);
                removedClusters.Add(cluster);
            }
            var reattached = ReattachOrphanedTabs(removedClusters, canonical);
            foreach (var removed in removedClusters)
                if (!reattached.Contains(removed.Id)) CloseTabsForCluster(removed.Id);
        }
        finally
        {
            isReconcilingClusters = false;
        }
        RebuildOpenedClustersMap();
        ReconcileOpenClusterMenu();
        return checks;
    }

    /// <summary>
    /// When a cluster disappears from a client's discovered list (e.g. a placeholder created while
    /// the client was unreachable gets replaced once discovery succeeds), any already-open tabs
    /// pointing at the removed cluster would otherwise be stuck forever with a stale/invalid
    /// identity. If the client now resolves to exactly one real cluster, re-point those tabs at it
    /// so they recover without the user needing to close and reopen them.
    /// </summary>
    private HashSet<string> ReattachOrphanedTabs(IReadOnlyList<ClusterViewModel> removedClusters, IReadOnlyList<ClusterViewModel> loadedClusters)
    {
        var reattached = new HashSet<string>(StringComparer.Ordinal);
        foreach (var removed in removedClusters.Where(c => c.IsUnavailablePlaceholder))
        {
            var replacements = loadedClusters.Where(c => c.Client.Name == removed.Client.Name &&
                !c.IsUnavailablePlaceholder && c.IsAvailable).ToList();
            if (replacements.Count != 1) continue;
            foreach (var opened in OpenedClusters.Where(o => o.ClusterId == removed.Id).ToArray())
                opened.ReattachCluster(replacements[0]);
            reattached.Add(removed.Id);
        }
        return reattached;
    }

    private async Task RefreshClustersAsync(CancellationToken cancellationToken)
    {
        // Periodic health check: single attempt per cluster, no retries.
        var selected = await Dispatcher.UIThread.InvokeAsync(() => Clusters.Where(c => IsClusterAvailable(c) && !c.IsUnavailablePlaceholder).ToArray());
        await Task.WhenAll(selected.Select(c => CheckConnectionSafeAsync(c, allowRetries: false, periodic: true, cancellationToken)));
    }

    private async Task CheckConnectionSafeAsync(ClusterViewModel cluster, bool allowRetries = true,
        bool periodic = false, CancellationToken cancellationToken = default)
    {
        if (!cluster.IsAvailable || cluster.IsUnavailablePlaceholder) return;
        if (periodic)
        {
            AppLogService.LogInfo($"Periodic connection check started for cluster {cluster.Name} ({cluster.Id})", "Connection");
            Log.Information("Periodic connection check started for cluster {ClusterName} ({ClusterId})", cluster.Name, cluster.Id);
        }
        try
        {
            await cluster.CheckConnectionAsync(allowRetries, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed connection check for cluster {ClusterName}", cluster.Name);
            await Dispatcher.UIThread.InvokeAsync(() => cluster.ApplyDiagnosticStatus(ConnectionState.Failed, ex.Message));
        }
    }

    private async Task DiscoverClientsNeedingRefreshAsync(CancellationToken cancellationToken)
    {
        var clientNames = await Dispatcher.UIThread.InvokeAsync(() => GetClientsNeedingDiscovery(ClientFactory.GetAllClients()));
        if (clientNames.Count == 0) return;
        cancellationToken.ThrowIfCancellationRequested();
        var discovered = await clusterFactory.LoadClustersForClientsAsync(clientNames);
        var checks = await Dispatcher.UIThread.InvokeAsync(() => ApplyClusterSnapshotForClients(discovered, clientNames));
        await Task.WhenAll(checks.Select(c => CheckConnectionSafeAsync(c, allowRetries: false, cancellationToken: cancellationToken)));
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
        RefreshAvailability();
        if (!IsClientEnabled(clientName) && clientName != "Local") return;
        await clusterRefreshLock.WaitAsync();
        try
        {
            var names = new HashSet<string>(StringComparer.Ordinal) { clientName };
            var discovered = await clusterFactory.LoadClustersForClientsAsync(names);
            var checks = await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var pending = ApplyClusterSnapshotForClients(discovered, names);
                pending.AddRange(Clusters.Where(c => c.Client.Name == clientName && c.IsAvailable &&
                    !c.IsUnavailablePlaceholder && c.Status != ConnectionState.Connected));
                return pending.Distinct().ToArray();
            });
            await Task.WhenAll(checks.Select(c => CheckConnectionSafeAsync(c, allowRetries: false)));
            foreach (var opened in OpenedClusters.Where(o => checks.Any(c => c.Id == o.ClusterId)).ToArray())
                await opened.LoadTopicsAsync();
        }
        finally
        {
            clusterRefreshLock.Release();
        }
    }

    private HashSet<string> GetClientsNeedingDiscovery(IReadOnlyList<IKafkaLensClient> clients)
    {
        return clients.Where(client => client.Name != "Local" && IsClientEnabled(client.Name))
            .Where(client => !Clusters.Any(c => c.Client.Name == client.Name) ||
                Clusters.Any(c => c.Client.Name == client.Name && c.IsAvailable && c.IsUnavailablePlaceholder))
            .Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
    }

    private void UpdateOpenedClusters()
    {
        foreach (var opened in OpenedClusters)
        {
            var cluster = Clusters.FirstOrDefault(c => c.Id == opened.ClusterId);
            if (cluster != null) opened.UpdateClusterName(cluster.Name);
        }
    }

    private void OnClustersChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        foreach (var removed in subscribedClusters.Where(c => !Clusters.Contains(c)).ToArray())
        {
            removed.PropertyChanged -= OnClusterPropertyChanged;
            removed.Detach();
            RemoveClusterFromMenu(removed);
            subscribedClusters.Remove(removed);
            if (!isReconcilingClusters) CloseTabsForCluster(removed.Id);
        }
        foreach (var added in Clusters.Where(c => !subscribedClusters.Contains(c)))
        {
            subscribedClusters.Add(added);
            ApplyClientAvailability(added);
            added.PropertyChanged += OnClusterPropertyChanged;
            if (IsClusterAvailable(added)) AddClusterToMenu(added);
        }
    }

    private static string GetClusterKey(ClusterViewModel cluster) => $"{cluster.Client.Name}:{cluster.Id}";

    private void ApplyLoadedStatus(ClusterViewModel existing, ClusterViewModel loaded)
    {
        existing.ApplyDiagnosticStatus(loaded.Status, loaded.LastError);
    }
}
