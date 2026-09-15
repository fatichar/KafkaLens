using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Threading;
using KafkaLens.Clients.Entities;
using KafkaLens.Shared;
using KafkaLens.Shared.DataAccess;
using KafkaLens.Shared.Entities;
using KafkaLens.Shared.Models;

namespace KafkaLens.ViewModels;

/// <summary>Stages all edits made in the dialog until SaveAsync is called.</summary>
public sealed class EditClustersViewModel : IDisposable
{
    private readonly IClusterInfoRepository ClusterRepository;
    private readonly IClientInfoRepository ClientRepository;
    private readonly IClientFactory ClientFactory;
    private readonly Func<string, Task> RefreshClustersForClient;
    private readonly ObservableCollection<ClusterViewModel> AllClusters;
    private readonly Dictionary<string, ClusterInfo> originalClusters;
    private readonly Dictionary<string, ClientInfo> originalClients;
    private readonly HashSet<string> removedClusterIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> removedClientIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> addedClusterIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> addedClientIds = new(StringComparer.Ordinal);
    private readonly Dictionary<ClusterViewModel, PropertyChangedEventHandler> liveClusterStatusHandlers = new();
    private readonly Dictionary<IKafkaLensClient, string?> clientOwnerIds = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, ClientChange> pendingClientChanges = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> pendingClientRefreshes = new(StringComparer.Ordinal);
    private readonly HashSet<string> pendingClusterChecks = new(StringComparer.Ordinal);
    private bool clientsNeedReload;
    private bool disposed;

    public ObservableCollection<ClusterViewModel> Clusters { get; }
    public ObservableCollection<ClientInfoViewModel> Clients { get; }
    public Func<ClusterViewModel, Task<bool>>? ConfirmDisableCluster { get; set; }
    public Func<ClientInfoViewModel, Task<bool>>? ConfirmDisableClient { get; set; }
    public Action<string>? CloseTabsForCluster { get; set; }
    public Action<string>? CloseTabsForClient { get; set; }
    public Func<string, bool>? HasOpenTabsForCluster { get; set; }
    public Func<string, bool>? HasOpenTabsForClient { get; set; }
    public Action? AvailabilityChanged { get; set; }

    private IKafkaLensClient LocalClient => field ??= ClientFactory.GetClient("Local");

    public EditClustersViewModel(ObservableCollection<ClusterViewModel> clusters,
        IClusterInfoRepository clusterInfoRepository, IClientInfoRepository clientInfoRepository,
        IClientFactory clientFactory, Func<string, Task> refreshClustersForClient)
    {
        AllClusters = clusters;
        ClusterRepository = clusterInfoRepository;
        ClientRepository = clientInfoRepository;
        ClientFactory = clientFactory;
        RefreshClustersForClient = refreshClustersForClient;
        originalClusters = (clusterInfoRepository.GetAll()?.Values ?? Enumerable.Empty<ClusterInfo>()).ToDictionary(c => c.Id, CopyCluster);
        originalClients = (clientInfoRepository.GetAll()?.Values ?? Enumerable.Empty<ClientInfo>()).ToDictionary(c => c.Id, CopyClient);
        Clusters = new ObservableCollection<ClusterViewModel>(clusters
            .Where(c => c.Client.CanEditClusters).Select(CopyCluster));
        Clients = new ObservableCollection<ClientInfoViewModel>(originalClients.Values.Select(c => new ClientInfoViewModel(CopyClient(c))));
        foreach (var client in Clients)
            client.PropertyChanged += OnStagedClientChanged;
        AllClusters.CollectionChanged += OnAllClustersChanged;
        foreach (var cluster in AllClusters)
            SubscribeToLiveClusterStatus(cluster);
        ApplyStagedAvailability();
        ApplyExistingClientStatuses();
    }

    private static ClusterInfo CopyCluster(ClusterInfo c) => new(c.Id, c.Name, c.Address, c.Protocol) { IsEnabled = c.IsEnabled };
    private static ClientInfo CopyClient(ClientInfo c) => new(c.Id, c.Name, c.Address, c.Protocol) { IsEnabled = c.IsEnabled };
    private static ClusterViewModel CopyCluster(ClusterViewModel c) => new(new KafkaCluster(c.Id, c.Name, c.Address) { IsEnabled = c.IsEnabled, Status = c.Status, LastError = c.LastError }, c.Client) { IsClientEnabled = c.IsClientEnabled };

    private void Dispatch(Action action)
    {
        if (disposed) return;
        if (Dispatcher.UIThread.CheckAccess()) action();
        else Dispatcher.UIThread.Post(() => { if (!disposed) action(); });
    }

    private static bool IsBuiltInLocal(IKafkaLensClient client) => client.Name == "Local" && client.CanEditClusters;

    private string? GetOwnerId(IKafkaLensClient client)
    {
        if (IsBuiltInLocal(client)) return null;
        if (!clientOwnerIds.TryGetValue(client, out var id))
        {
            id = originalClients.Values.FirstOrDefault(c => c.Name == client.Name)?.Id;
            clientOwnerIds[client] = id;
        }
        return id;
    }

    private bool MatchesOwner(IKafkaLensClient client, string id, string name)
        => !IsBuiltInLocal(client) && (GetOwnerId(client) is { } ownerId
            ? ownerId == id : !originalClients.ContainsKey(id) && client.Name == name);

    private bool IsOwnerEnabled(IKafkaLensClient client, bool staged)
    {
        var id = GetOwnerId(client);
        if (id == null) return true;
        return staged ? Clients.FirstOrDefault(c => c.Id == id)?.IsEnabled ?? false
            : ClientRepository.GetAll()?.Values.FirstOrDefault(c => c.Id == id)?.IsEnabled ?? false;
    }

    private bool IsBuiltInName(string name) => name == "Local" &&
        (AllClusters.Any(c => IsBuiltInLocal(c.Client)) || (ClientFactory.GetAllClients()?.Any(IsBuiltInLocal) ?? false));

    private bool HasClientTabs(string id, string name) => !IsBuiltInName(name)
        ? HasOpenTabsForClient?.Invoke(name) ?? false
        : AllClusters.Any(c => MatchesOwner(c.Client, id, name) && (HasOpenTabsForCluster?.Invoke(c.Id) ?? false));

    private void CloseClientTabs(string id, string name)
    {
        if (!IsBuiltInName(name)) CloseTabsForClient?.Invoke(name);
        else foreach (var cluster in AllClusters.Where(c => MatchesOwner(c.Client, id, name)))
            CloseTabsForCluster?.Invoke(cluster.Id);
    }

    private void OnStagedClientChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(ClientInfoViewModel.IsEnabled)) return;
        Dispatch(() =>
        {
            ApplyStagedAvailability();
            ApplyExistingClientStatuses();
        });
    }

    private void ApplyStagedAvailability()
    {
        foreach (var staged in Clusters)
        {
            staged.IsClientEnabled = IsOwnerEnabled(staged.Client, true);
            var live = AllClusters.FirstOrDefault(c => c.Id == staged.Id);
            if (live != null) MirrorStatus(live);
        }
    }

    private void MirrorStatus(ClusterViewModel live)
    {
        var staged = Clusters.FirstOrDefault(c => c.Id == live.Id);
        if (staged == null) return;
        staged.IsClientEnabled = IsOwnerEnabled(live.Client, true);
        if (!staged.IsAvailable)
        {
            staged.SetDisabledStatus();
            return;
        }
        staged.LastError = live.LastError;
        staged.Status = live.Status;
        staged.StatusColor = live.StatusColor;
        staged.ConnectionStatus = live.ConnectionStatus;
        staged.IsChecking = live.IsChecking;
    }

    private void SubscribeToLiveClusterStatus(ClusterViewModel liveCluster)
    {
        if (liveClusterStatusHandlers.ContainsKey(liveCluster)) return;
        GetOwnerId(liveCluster.Client);
        PropertyChangedEventHandler handler = (_, args) =>
        {
            if (args.PropertyName is not (nameof(ClusterViewModel.Status)
                or nameof(ClusterViewModel.LastError)
                or nameof(ClusterViewModel.StatusColor)
                or nameof(ClusterViewModel.ConnectionStatus)
                or nameof(ClusterViewModel.IsChecking)
                or nameof(ClusterViewModel.IsAvailable))) return;
            Dispatch(() =>
            {
                if (!liveClusterStatusHandlers.ContainsKey(liveCluster) || !AllClusters.Contains(liveCluster)) return;
                MirrorStatus(liveCluster);
                ApplyExistingClientStatuses();
            });
        };
        liveCluster.PropertyChanged += handler;
        liveClusterStatusHandlers[liveCluster] = handler;
    }

    private void UnsubscribeFromLiveClusterStatus(ClusterViewModel liveCluster)
    {
        if (!liveClusterStatusHandlers.Remove(liveCluster, out var handler)) return;
        liveCluster.PropertyChanged -= handler;
    }

    private void OnAllClustersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatch(() =>
        {
            foreach (var old in liveClusterStatusHandlers.Keys.Where(c => !AllClusters.Contains(c)).ToArray())
                UnsubscribeFromLiveClusterStatus(old);
            foreach (var staged in Clusters.Where(c => !addedClusterIds.Contains(c.Id) && AllClusters.All(l => l.Id != c.Id)).ToArray())
                Clusters.Remove(staged);
            foreach (var live in AllClusters.ToArray())
            {
                SubscribeToLiveClusterStatus(live);
                if (live.Client.CanEditClusters && !removedClusterIds.Contains(live.Id) && Clusters.All(c => c.Id != live.Id))
                    Clusters.Add(CopyCluster(live));
            }
            ApplyStagedAvailability();
            ApplyExistingClientStatuses();
        });
    }

    private IEnumerable<ClusterViewModel> ClientMembers(ClientInfoViewModel client)
    {
        return AllClusters.Where(c => MatchesOwner(c.Client, client.Id, client.Name) && c.IsEnabled
            && IsOwnerEnabled(c.Client, false)
            && (Clusters.FirstOrDefault(s => s.Id == c.Id)?.IsEnabled ?? true));
    }

    private void ApplyExistingClientStatuses()
    {
        foreach (var client in Clients)
        {
            if (!client.IsEnabled) { client.SetDisabledStatus(); continue; }
            var clusters = ClientMembers(client).ToArray();
            client.LastError = clusters.FirstOrDefault(c => c.Status == ConnectionState.Failed)?.LastError;
            client.Status = clusters.Any(c => c.Status == ConnectionState.Failed) ? ConnectionState.Failed
                : clusters.Length > 0 && clusters.All(c => c.Status == ConnectionState.Connected) ? ConnectionState.Connected
                : clusters.Any(c => c.Status == ConnectionState.Checking) ? ConnectionState.Checking
                : ConnectionState.Unknown;
        }
    }

    private void ApplyClientConnectionResult(string id, string name, IReadOnlyDictionary<ClusterViewModel, int> targets, IReadOnlyList<KafkaCluster> results, ConnectionState state, string? error = null)
    {
        var byId = results.Where(c => !c.IsUnavailablePlaceholder).GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());
        foreach (var live in targets.Keys.Where(c => AllClusters.Contains(c) && c.ConnectionVersion == targets[c] && MatchesOwner(c.Client, id, name)))
        {
            if (!IsOwnerEnabled(live.Client, false) || !live.IsAvailable) { MirrorStatus(live); continue; }
            if (byId.TryGetValue(live.Id, out var result))
                live.ApplyDiagnosticStatus(result.Status, result.LastError);
            else if (state == ConnectionState.Failed)
                live.ApplyDiagnosticStatus(ConnectionState.Failed, error ?? "KafkaLens client is unavailable.");
            MirrorStatus(live);
        }
        ApplyExistingClientStatuses();
    }

    public Task<ConnectionValidationResult> TestConnectionAsync(string address) => TestConnectionCoreAsync(address);

    public Task<ConnectionValidationResult> TestConnectionAsync(ClusterViewModel cluster, string address)
    {
        var staged = Clusters.FirstOrDefault(c => c.Id == cluster.Id);
        if (staged != null && (!staged.IsAvailable || !IsOwnerEnabled(staged.Client, false)))
            return Task.FromResult(ConnectionValidationResult.Failed("Cluster or client is disabled."));
        return TestConnectionCoreAsync(address, staged != null && staged.Address.Trim() == address.Trim() ? staged : null);
    }

    private async Task<ConnectionValidationResult> TestConnectionCoreAsync(string address, ClusterViewModel? staged = null)
    {
        if (!IsOwnerEnabled(staged?.Client ?? LocalClient, false)
            || !IsOwnerEnabled(staged?.Client ?? LocalClient, true) || staged is { IsAvailable: false })
            return ConnectionValidationResult.Failed("Cluster or client is disabled.");
        var live = staged == null ? null : AllClusters.FirstOrDefault(c => c.Id == staged.Id && c.Address.Trim() == address.Trim());
        var liveVersion = live?.ConnectionVersion;
        var stagedVersion = staged?.ConnectionVersion;
        try
        {
            var result = LocalClient is IConnectionTestClient diagnostic
                ? await diagnostic.ValidateConnectionWithDetailsAsync(address)
                : (await LocalClient.ValidateConnectionAsync(address) ? ConnectionValidationResult.Success() : ConnectionValidationResult.Failed("Connection validation returned false."));
            Dispatch(() => ApplyClusterTestResult(staged, live, stagedVersion, liveVersion, result.Succeeded ? ConnectionState.Connected : ConnectionState.Failed, result.ErrorMessage));
            return result;
        }
        catch (Exception e)
        {
            Dispatch(() => ApplyClusterTestResult(staged, live, stagedVersion, liveVersion, ConnectionState.Failed, e.Message));
            throw;
        }
    }

    private void ApplyClusterTestResult(ClusterViewModel? staged, ClusterViewModel? live, int? stagedVersion, int? liveVersion, ConnectionState state, string? error)
    {
        if (staged == null || !Clusters.Contains(staged)) return;
        if (live != null)
        {
            if (!AllClusters.Contains(live) || live.ConnectionVersion != liveVersion) return;
            live.ApplyDiagnosticStatus(state, error);
            if (staged.ConnectionVersion == stagedVersion) MirrorStatus(live);
        }
        else if (staged.ConnectionVersion == stagedVersion) staged.ApplyDiagnosticStatus(state, error);
    }

    public Task<ConnectionValidationResult> TestClientConnectionAsync(string address, string protocol = "grpc")
        => TestClientConnectionCoreAsync(new ClientInfo(Guid.NewGuid().ToString(), "Connection test", address.Trim(), protocol));

    public Task<ConnectionValidationResult> TestClientConnectionAsync(ClientInfoViewModel client, string address)
    {
        if (!client.IsEnabled) return Task.FromResult(ConnectionValidationResult.Failed("Client is disabled."));
        var original = originalClients.GetValueOrDefault(client.Id);
        var targetName = original?.Name ?? client.Name;
        var matchesLive = (original?.Address ?? client.Address).Trim() == address.Trim()
            && (original?.Protocol ?? client.Protocol) == client.Protocol;
        return TestClientConnectionCoreAsync(new ClientInfo(client.Id, client.Name, address.Trim(), client.Protocol),
            matchesLive ? targetName : null, client.Address.Trim() == address.Trim() ? client : null);
    }

    private async Task<ConnectionValidationResult> TestClientConnectionCoreAsync(ClientInfo info, string? liveName = null, ClientInfoViewModel? row = null)
    {
        if (liveName != null && ClientRepository.GetAll()?.Values.FirstOrDefault(c => c.Id == info.Id) is { IsEnabled: false })
            return ConnectionValidationResult.Failed("Client is disabled.");
        var targets = AllClusters.Where(c => liveName != null && MatchesOwner(c.Client, info.Id, liveName))
            .ToDictionary(c => c, c => c.ConnectionVersion);
        var stagedRow = row != null && Clients.Contains(row);
        bool IsCurrentResult() => (row == null || (row.Address.Trim() == info.Address && row.Protocol == info.Protocol
            && (!stagedRow || Clients.Contains(row))))
            && (targets.Count == 0 || targets.Any(t => AllClusters.Contains(t.Key) && t.Key.ConnectionVersion == t.Value));
        try
        {
            var clusters = (await ClientFactory.TestConnectionAsync(info)).ToArray();
            var enabledResults = clusters.Where(c => liveName == null
                || (AllClusters.FirstOrDefault(l => MatchesOwner(l.Client, info.Id, liveName) && l.Id == c.Id) is not { IsEnabled: false }
                    && Clusters.FirstOrDefault(s => s.Id == c.Id) is not { IsEnabled: false })).ToArray();
            var state = GetClientConnectionState(enabledResults);
            var error = clusters.FirstOrDefault(c => c.IsEnabled && c.Status == ConnectionState.Failed)?.LastError;
            Dispatch(() =>
            {
                if (!IsCurrentResult()) return;
                if (liveName != null) ApplyClientConnectionResult(info.Id, liveName, targets, clusters, state, error);
                if (row != null && (liveName == null || !Clients.Contains(row) || !ClientMembers(row).Any()))
                {
                    if (state != ConnectionState.Unknown) { row.LastError = error; row.Status = state; }
                }
            });
            return state == ConnectionState.Connected ? ConnectionValidationResult.Success()
                : ConnectionValidationResult.Failed(error ?? "Cluster connectivity could not be confirmed.");
        }
        catch (Exception e)
        {
            Dispatch(() =>
            {
                if (!IsCurrentResult()) return;
                if (liveName != null) ApplyClientConnectionResult(info.Id, liveName, targets, Array.Empty<KafkaCluster>(), ConnectionState.Failed, e.Message);
                if (row != null) { row.LastError = e.Message; row.Status = ConnectionState.Failed; }
            });
            throw;
        }
    }

    private static ConnectionState GetClientConnectionState(IReadOnlyList<KafkaCluster> clusters)
    {
        var enabled = clusters.Where(c => c.IsEnabled).ToArray();
        if (enabled.Any(c => c.Status == ConnectionState.Failed || c.IsUnavailablePlaceholder)) return ConnectionState.Failed;
        return enabled.Length > 0 && enabled.All(c => c.Status == ConnectionState.Connected)
            ? ConnectionState.Connected : ConnectionState.Unknown;
    }

    public async Task AddClusterImmediatelyAsync(string name, string address)
    {
        await StageAddClusterAsync(name, address);
        await SaveCoreAsync(Clusters.Last().Id, null);
    }

    public Task StageAddClusterAsync(string name, string address)
    {
        var info = ClusterInfo.Create(name, address);
        addedClusterIds.Add(info.Id);
        Clusters.Add(new ClusterViewModel(new KafkaCluster(info.Id, name, address) { IsEnabled = true }, LocalClient)
            { IsClientEnabled = IsOwnerEnabled(LocalClient, true) });
        return Task.CompletedTask;
    }

    public Task StageUpdateCluster(ClusterViewModel cluster, string name, string address)
    {
        var row = Clusters.FirstOrDefault(c => c.Id == cluster.Id) ?? throw new ArgumentException("Cluster is not staged in this dialog.", nameof(cluster));
        row.Name = name;
        row.Address = address;
        return Task.CompletedTask;
    }

    public async Task UpdateClusterImmediatelyAsync(ClusterViewModel cluster, string name, string address)
    {
        await StageUpdateCluster(cluster, name, address);
        await SaveCoreAsync(cluster.Id, null);
    }

    public void RemoveClusterImmediately(ClusterViewModel? cluster)
    {
        if (cluster == null) return;
        StageRemoveCluster(cluster);
        DeleteCluster(cluster.Id);
        AvailabilityChanged?.Invoke();
    }

    private void DeleteCluster(string id)
    {
        if (originalClusters.ContainsKey(id)) ClusterRepository.Delete(id);
        CloseTabsForCluster?.Invoke(id);
        var live = AllClusters.FirstOrDefault(c => c.Id == id);
        if (live != null) AllClusters.Remove(live);
        originalClusters.Remove(id);
        removedClusterIds.Remove(id);
        addedClusterIds.Remove(id);
    }

    public void StageRemoveCluster(ClusterViewModel? cluster)
    {
        var row = Clusters.FirstOrDefault(c => c.Id == cluster?.Id);
        if (row == null) return;
        if (!addedClusterIds.Remove(row.Id)) removedClusterIds.Add(row.Id);
        Clusters.Remove(row);
    }

    public async Task AddClientImmediatelyAsync(string name, string address, string protocol = "grpc")
    {
        await StageAddClientAsync(name, address, protocol);
        await SaveCoreAsync(null, Clients.Last().Id);
    }

    public Task StageAddClientAsync(string name, string address, string protocol = "grpc")
    {
        var info = new ClientInfo(Guid.NewGuid().ToString(), name, address, protocol);
        addedClientIds.Add(info.Id);
        var row = new ClientInfoViewModel(info);
        row.PropertyChanged += OnStagedClientChanged;
        Clients.Add(row);
        return Task.CompletedTask;
    }

    public Task StageUpdateClient(ClientInfo updated)
    {
        var existing = Clients.FirstOrDefault(c => c.Id == updated.Id);
        existing?.UpdateInfo(CopyClient(updated));
        return Task.CompletedTask;
    }

    public void StageRemoveClient(ClientInfoViewModel? client)
    {
        var row = Clients.FirstOrDefault(c => c.Id == client?.Id);
        if (row == null) return;
        if (!addedClientIds.Remove(row.Id)) removedClientIds.Add(row.Id);
        row.PropertyChanged -= OnStagedClientChanged;
        Clients.Remove(row);
        ApplyStagedAvailability();
    }

    private sealed record ClusterChange(string Id, string Name, string Address, string? Protocol, bool Enabled, bool Added, bool AddressChanged, bool NewlyEnabled);
    private sealed record ClientChange(string Id, string Name, string Address, string Protocol, bool Enabled, bool Added, string? OldName, bool TransportChanged, bool NewlyEnabled);

    public Task SaveAsync() => SaveCoreAsync(null, null);

    private async Task SaveCoreAsync(string? onlyClusterId, string? onlyClientId)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var clusterRows = Clusters.Where(c => onlyClientId == null && (onlyClusterId == null || c.Id == onlyClusterId)).ToArray();
        var clientRows = Clients.Where(c => onlyClusterId == null && (onlyClientId == null || c.Id == onlyClientId)).ToArray();
        foreach (var row in clusterRows)
        {
            if (originalClusters.GetValueOrDefault(row.Id) is { IsEnabled: true } && !row.IsEnabled
                && (HasOpenTabsForCluster?.Invoke(row.Id) ?? false) && ConfirmDisableCluster != null && !await ConfirmDisableCluster(row))
                row.IsEnabled = true;
        }
        foreach (var row in clientRows)
        {
            if (originalClients.GetValueOrDefault(row.Id) is { IsEnabled: true } old && !row.IsEnabled
                && HasClientTabs(row.Id, old.Name) && ConfirmDisableClient != null && !await ConfirmDisableClient(row))
                row.IsEnabled = true;
        }
        var clusterChanges = clusterRows.Select(row =>
        {
            var old = originalClusters.GetValueOrDefault(row.Id);
            return new ClusterChange(row.Id, row.Name, row.Address, old?.Protocol, row.IsEnabled, addedClusterIds.Contains(row.Id),
                old != null && old.Address != row.Address, old is { IsEnabled: false } && row.IsEnabled);
        }).Where(c => c.Added || originalClusters.GetValueOrDefault(c.Id) is { } old
            && (old.Name != c.Name || old.Address != c.Address || old.IsEnabled != c.Enabled)).ToArray();
        var clientChanges = clientRows.Select(row =>
        {
            var old = originalClients.GetValueOrDefault(row.Id);
            return new ClientChange(row.Id, row.Name, row.Address, row.Protocol, row.IsEnabled, addedClientIds.Contains(row.Id), old?.Name,
                old != null && (old.Address != row.Address || old.Protocol != row.Protocol), old is { IsEnabled: false } && row.IsEnabled);
        }).Where(c => c.Added || originalClients.GetValueOrDefault(c.Id) is { } old
            && (old.Name != c.Name || c.TransportChanged || old.IsEnabled != c.Enabled)).ToArray();
        var clusterRemovals = removedClusterIds.Where(id => onlyClientId == null && (onlyClusterId == null || id == onlyClusterId)).ToArray();
        var clientRemovals = removedClientIds.Where(id => onlyClusterId == null && (onlyClientId == null || id == onlyClientId)).ToArray();
        if (clusterChanges.Length + clientChanges.Length + clusterRemovals.Length + clientRemovals.Length == 0
            && !clientsNeedReload && pendingClientChanges.Count + pendingClientRefreshes.Count + pendingClusterChecks.Count == 0)
        {
            foreach (var live in AllClusters) MirrorStatus(live);
            ApplyExistingClientStatuses();
            return;
        }

        foreach (var id in clusterRemovals) DeleteCluster(id);
        foreach (var id in clientRemovals)
        {
            if (originalClients.TryGetValue(id, out var old))
            {
                ClientRepository.Delete(id);
                clientsNeedReload = true;
                CloseClientTabs(id, old.Name);
                foreach (var live in AllClusters.Where(c => MatchesOwner(c.Client, id, old.Name)).ToArray()) AllClusters.Remove(live);
            }
            originalClients.Remove(id);
            removedClientIds.Remove(id);
            pendingClientChanges.Remove(id);
            pendingClientRefreshes.Remove(id);
        }
        foreach (var change in clientChanges)
        {
            var info = new ClientInfo(change.Id, change.Name, change.Address, change.Protocol) { IsEnabled = change.Enabled };
            if (change.Added) ClientRepository.Add(info); else ClientRepository.Update(info);
            originalClients[change.Id] = CopyClient(info);
            addedClientIds.Remove(change.Id);
            clientsNeedReload = true;
            var pending = pendingClientChanges.GetValueOrDefault(change.Id);
            pendingClientChanges[change.Id] = pending == null ? change : change with
            {
                OldName = pending.OldName,
                Added = pending.Added,
                TransportChanged = pending.TransportChanged || change.TransportChanged,
                NewlyEnabled = pending.NewlyEnabled || change.NewlyEnabled
            };
            if (!change.Enabled)
            {
                pendingClientRefreshes.Remove(change.Id);
                if (change.OldName != null) CloseClientTabs(change.Id, change.OldName);
            }
        }
        foreach (var change in clusterChanges)
        {
            var info = new ClusterInfo(change.Id, change.Name, change.Address, change.Protocol) { IsEnabled = change.Enabled };
            if (change.Added) ClusterRepository.Add(info); else ClusterRepository.Update(info);
            var live = AllClusters.FirstOrDefault(c => c.Id == change.Id);
            if (live == null && change.Added)
            {
                live = new ClusterViewModel(new KafkaCluster(change.Id, change.Name, change.Address) { IsEnabled = change.Enabled }, LocalClient);
                AllClusters.Add(live);
            }
            if (live != null)
            {
                live.Name = change.Name;
                live.Address = change.Address;
                live.IsEnabled = change.Enabled;
                if (!change.Enabled) CloseTabsForCluster?.Invoke(change.Id);
            }
            originalClusters[change.Id] = CopyCluster(info);
            addedClusterIds.Remove(change.Id);
            if (change.Enabled && (change.Added || change.AddressChanged || change.NewlyEnabled)) pendingClusterChecks.Add(change.Id);
            else if (!change.Enabled) pendingClusterChecks.Remove(change.Id);
        }
        if (clientsNeedReload)
        {
            await ClientFactory.LoadClientsAsync();
            foreach (var change in pendingClientChanges.Values.ToArray())
            {
                if (!IsBuiltInName(change.Name))
                {
                    var refreshedClient = ClientFactory.GetClient(change.Name);
                    var targets = AllClusters.Concat(Clusters).Where(c => GetOwnerId(c.Client) == change.Id).ToArray();
                    clientOwnerIds[refreshedClient] = change.Id;
                    foreach (var live in targets) live.ReplaceClient(refreshedClient, resetTopics: change.TransportChanged);
                    if (change.Enabled && (change.Added || change.TransportChanged || change.NewlyEnabled || pendingClientRefreshes.ContainsKey(change.Id)))
                        pendingClientRefreshes[change.Id] = change.Name;
                }
                pendingClientChanges.Remove(change.Id);
            }
            clientsNeedReload = false;
        }
        // AvailabilityChanged re-enters MainViewModel.RefreshAvailability, which can close tabs and
        // remove clusters -- mutating AllClusters while we are still working through it. Snapshot
        // before notifying so the iteration below cannot throw on a concurrent modification.
        foreach (var live in AllClusters.ToArray()) live.IsClientEnabled = IsOwnerEnabled(live.Client, false);
        ApplyStagedAvailability();
        AvailabilityChanged?.Invoke();
        foreach (var (id, name) in pendingClientRefreshes.ToArray())
        {
            await RefreshClustersForClient(name);
            pendingClientRefreshes.Remove(id);
            pendingClusterChecks.RemoveWhere(clusterId => AllClusters.Any(c => c.Id == clusterId && GetOwnerId(c.Client) == id));
        }
        foreach (var id in pendingClusterChecks.ToArray())
        {
            var live = AllClusters.FirstOrDefault(c => c.Id == id);
            if (live != null && live.IsAvailable && IsOwnerEnabled(live.Client, false))
                await live.CheckConnectionAsync(false);
            pendingClusterChecks.Remove(id);
        }
        foreach (var live in AllClusters.ToArray()) MirrorStatus(live);
        ApplyExistingClientStatuses();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        AllClusters.CollectionChanged -= OnAllClustersChanged;
        foreach (var (cluster, handler) in liveClusterStatusHandlers.ToArray())
            cluster.PropertyChanged -= handler;
        liveClusterStatusHandlers.Clear();
        foreach (var client in Clients.ToArray()) client.PropertyChanged -= OnStagedClientChanged;
    }
}
