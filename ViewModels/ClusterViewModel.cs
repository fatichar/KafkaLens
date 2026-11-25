using System.Collections.ObjectModel;
using System.Threading;
using System.Diagnostics;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KafkaLens.Shared.Models;
using KafkaLens.Shared;
using KafkaLens.Shared.Telemetry;
using KafkaLens.ViewModels.Services;
using KafkaLens.ViewModels.Telemetry;

namespace KafkaLens.ViewModels;

public enum TopicLoadState
{
    NotLoaded,
    Loading,
    Loaded,
    Failed
}

public sealed partial class ClusterViewModel: ConnectionViewModelBase
{
    private const int TOPIC_LOAD_RETRY_COUNT = 2;
    private static readonly TimeSpan TopicLoadRetryDelay = TimeSpan.FromMilliseconds(750);

    public IKafkaLensClient Client { get; private set; }
    public IAsyncRelayCommand LoadTopicsCommand { get; }
    private readonly KafkaCluster cluster;
    private readonly IAppLogService? appLogService;
    private readonly ITelemetry telemetry;
    private readonly SemaphoreSlim connectionCheckLock = new(1, 1);
    private readonly Dictionary<(string Topic, int? Partition), string> messageFailures = new();
    private ConnectionState brokerStatus;
    private string? brokerError;
    private string? topicsError;
    private CancellationTokenSource connectionLifetime = new();
    public ObservableCollection<Topic> Topics { get; } = new();

    public string Id => cluster.Id;
    public bool IsUnavailablePlaceholder => cluster.IsUnavailablePlaceholder;
    public bool IsAvailable => IsEnabled && IsClientEnabled;
    internal int ConnectionVersion { get; private set; }
    internal bool HasMessageFailures => messageFailures.Count != 0;
    internal void Detach() => InvalidateOperations(resetEvidence: false);

    internal void UpdatePlaceholder(bool isPlaceholder)
    {
        if (cluster.IsUnavailablePlaceholder == isPlaceholder) return;
        cluster.IsUnavailablePlaceholder = isPlaceholder;
        InvalidateOperations(resetEvidence: true);
    }

    [ObservableProperty]
    private string name;

    [ObservableProperty]
    private string address;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveSchemaRegistryUrl))]
    private string? schemaRegistryUrl;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveSchemaRegistryUrl))]
    private string? clientSchemaRegistryUrl;

    public string? EffectiveSchemaRegistryUrl => string.IsNullOrWhiteSpace(ClientSchemaRegistryUrl)
        ? SchemaRegistryUrl
        : ClientSchemaRegistryUrl;

    /// <summary>
    /// Full registry connection used for decoding: URL plus credentials/CA for directly connected clusters
    /// (from the client, which reads them from the credential store), URL only otherwise.
    /// </summary>
    public KafkaLens.Formatting.SchemaRegistryOptions? EffectiveSchemaRegistry
    {
        get
        {
            if (string.IsNullOrWhiteSpace(EffectiveSchemaRegistryUrl)) return null;
            if (string.IsNullOrWhiteSpace(ClientSchemaRegistryUrl) && Client is ISchemaRegistryProvider provider
                && provider.GetSchemaRegistry(Id) is { } settings
                && settings.Url == EffectiveSchemaRegistryUrl.Trim())
                return new(settings.Url, settings.Username, settings.Password, settings.CaLocation);
            return new(EffectiveSchemaRegistryUrl.Trim());
        }
    }

    [ObservableProperty]
    private TopicLoadState topicLoadState = TopicLoadState.NotLoaded;

    [ObservableProperty]
    private bool isEnabled;

    [ObservableProperty]
    private bool isClientEnabled = true;

    partial void OnIsEnabledChanged(bool value)
    {
        cluster.IsEnabled = value;
        UpdateAvailability();
    }

    partial void OnIsClientEnabledChanged(bool value) => UpdateAvailability();

    private void UpdateAvailability()
    {
        InvalidateOperations(resetEvidence: IsAvailable);
        SetConnectionEnabled(IsAvailable);
        OnPropertyChanged(nameof(IsAvailable));
    }

    partial void OnAddressChanged(string value)
    {
        cluster.Address = value;
        InvalidateOperations(resetEvidence: true);
    }

    /// <summary>TLS/authentication settings (never secrets).</summary>
    public ClusterSecurity Security => cluster.Security ??= new ClusterSecurity();

    /// <summary>Short description for lists, e.g. "Plaintext" or "TLS".</summary>
    public string SecuritySummary => Security.Summary();

    /// <summary>Names of the secrets saved for this cluster.</summary>
    public IReadOnlyCollection<string> StoredSecretNames => cluster.StoredSecretNames;

    /// <summary>Secrets entered in the Edit Clusters dialog that are not saved yet.</summary>
    internal Dictionary<string, string>? PendingSecrets { get; set; }

    /// <summary>Replaces the security settings; resets connection state because the connection itself changed.</summary>
    public void UpdateSecurity(ClusterSecurity security, IReadOnlyCollection<string>? storedSecretNames = null)
    {
        var changed = !Security.SameAs(security);
        cluster.Security = security.Clone();
        if (storedSecretNames != null) cluster.StoredSecretNames = storedSecretNames.ToList();
        OnPropertyChanged(nameof(Security));
        OnPropertyChanged(nameof(SecuritySummary));
        OnPropertyChanged(nameof(StoredSecretNames));
        OnPropertyChanged(nameof(EffectiveSchemaRegistryUrl)); // registry credentials may have changed
        if (changed) InvalidateOperations(resetEvidence: true);
    }

    /// <summary>Call after the stored secrets changed so cached state does not hide a now-(in)valid login.</summary>
    internal void NotifySecretsChanged(IReadOnlyCollection<string> storedSecretNames)
    {
        cluster.StoredSecretNames = storedSecretNames.ToList();
        OnPropertyChanged(nameof(StoredSecretNames));
        OnPropertyChanged(nameof(EffectiveSchemaRegistryUrl));
        InvalidateOperations(resetEvidence: true);
    }

    private void InvalidateOperations(bool resetEvidence)
    {
        ConnectionVersion++;
        connectionLifetime.Cancel();
        connectionLifetime.Dispose();
        connectionLifetime = new CancellationTokenSource();
        topicsLoadTask = null;
        if (!resetEvidence) return;
        Topics.Clear();
        TopicLoadState = TopicLoadState.NotLoaded;
        messageFailures.Clear();
        topicsError = null;
        brokerError = null;
        brokerStatus = ConnectionState.Unknown;
        UpdateEffectiveStatus();
    }

    public ClusterViewModel(KafkaCluster cluster, IKafkaLensClient client, IAppLogService? appLogService = null,
        ITelemetry? telemetry = null)
    {
        Client = client;
        this.cluster = cluster;
        this.appLogService = appLogService;
        this.telemetry = telemetry ?? NullTelemetry.Instance;
        name = cluster.Name;
        address = cluster.Address;
        schemaRegistryUrl = cluster.SchemaRegistryUrl;
        isEnabled = cluster.IsEnabled;
        brokerStatus = cluster.Status;
        brokerError = cluster.LastError;
        Status = cluster.Status;
        LastError = cluster.LastError;
        SetConnectionEnabled(IsAvailable);
        LoadTopicsCommand = new AsyncRelayCommand(LoadTopicsAsync);
    }

    internal void ApplyDiagnosticStatus(ConnectionState status, string? error = null)
    {
        if (!IsAvailable) { SetDisabledStatus(); return; }
        if (status == ConnectionState.Unknown && Status != ConnectionState.Unknown) return;
        if (status == ConnectionState.Checking && Status != ConnectionState.Unknown) return;
        brokerStatus = status;
        brokerError = error;
        UpdateEffectiveStatus();
    }

    internal void ReportMessageResult(string topic, int? partition, Exception? error, int version)
    {
        if (!IsAvailable || version != ConnectionVersion) return;
        if (error != null)
            messageFailures[(topic, partition)] = error.Message;
        else
        {
            messageFailures.Remove((topic, partition));
            brokerStatus = ConnectionState.Connected;
            brokerError = null;
        }
        UpdateEffectiveStatus();
    }

    private void UpdateEffectiveStatus()
    {
        LastError = messageFailures.Values.FirstOrDefault() ?? topicsError ?? brokerError;
        Status = messageFailures.Count > 0 || topicsError != null ? ConnectionState.Failed : brokerStatus;
        cluster.Status = Status;
        cluster.LastError = LastError;
        SetConnectionEnabled(IsAvailable);
    }

    public Task CheckConnectionAsync(bool allowRetries = true, CancellationToken cancellationToken = default) =>
        CheckConnectionAsync(allowRetries, cancellationToken, recoverTopics: true);

    private async Task CheckConnectionAsync(bool allowRetries, CancellationToken cancellationToken, bool recoverTopics)
    {
        if (!IsAvailable || IsUnavailablePlaceholder) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connectionLifetime.Token);
        var token = linked.Token;
        var acquired = false;
        try
        {
            await connectionCheckLock.WaitAsync(token);
            acquired = true;
            var version = ConnectionVersion;
            await OnUiAsync(() =>
            {
                SetCheckingIfStatusIsUnknown();
                SetCheckingPresentation();
            });
            appLogService?.LogInfo($"Connecting to {Name}", "Connection");
            var result = Client is ICancellableConnectionClient cancellable
                ? await cancellable.ValidateConnectionWithDetailsAsync(Address, token)
                : await Client.ValidateConnectionAsync(Address).WaitAsync(token)
                    ? ConnectionValidationResult.Success()
                    : ConnectionValidationResult.Failed("Connection validation failed.");
            token.ThrowIfCancellationRequested();
            var isConnected = result.Succeeded;
            await OnUiAsync(() =>
            {
                if (!IsAvailable || version != ConnectionVersion) return;
                brokerStatus = isConnected ? ConnectionState.Connected : ConnectionState.Failed;
                brokerError = isConnected ? null : result.ErrorMessage;
                UpdateEffectiveStatus();
            });
            if (isConnected && recoverTopics && topicsError != null)
                await EnsureTopicsLoadedAsync(forceRefresh: true, logTopicLoad: true,
                    maxRetries: allowRetries ? TOPIC_LOAD_RETRY_COUNT : 0, cancellationToken: token, trigger: "health_check");
            appLogService?.LogInfo($"Connection check for {Name}: {ConnectionStatus}", "Connection");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (cancellationToken.IsCancellationRequested) throw;
        }
        catch (Exception e)
        {
            await OnUiAsync(() => ApplyDiagnosticStatus(ConnectionState.Failed, e.Message));
            appLogService?.LogError($"Could not connect to {Name}: {e.Message}", "Connection");
        }
        finally
        {
            if (acquired) connectionCheckLock.Release();
            await OnUiAsync(() => SetConnectionEnabled(IsAvailable));
        }
    }

    internal void ReplaceClient(IKafkaLensClient client, bool resetTopics)
    {
        if (ReferenceEquals(Client, client)) return;
        InvalidateOperations(resetEvidence: resetTopics);
        Client = client;
    }

    internal async Task RecheckConnectionAsync()
    {
        if (!IsAvailable) return;
        await CheckConnectionAsync(allowRetries: true, CancellationToken.None, recoverTopics: false);
        if (brokerStatus == ConnectionState.Connected)
            await EnsureTopicsLoadedAsync(forceRefresh: true, logTopicLoad: true, trigger: "retry");
    }

    private readonly object topicsLoadLock = new();
    private Task? topicsLoadTask;

    private async Task LoadTopicsAsync()
    {
        await EnsureTopicsLoadedAsync(forceRefresh: true, logTopicLoad: true, trigger: "sidebar");
    }

    internal async Task EnsureTopicsLoadedAsync(bool forceRefresh = false, bool logTopicLoad = false,
        int maxRetries = TOPIC_LOAD_RETRY_COUNT, CancellationToken cancellationToken = default, string trigger = "tab")
    {
        if (!IsAvailable || IsUnavailablePlaceholder) return;
        Task loadTask;
        lock (topicsLoadLock)
        {
            if (!forceRefresh && TopicLoadState == TopicLoadState.Loaded) return;
            if (topicsLoadTask == null || topicsLoadTask.IsCompleted)
                topicsLoadTask = LoadTopicsWithRetryAsync(logTopicLoad, maxRetries, ConnectionVersion, connectionLifetime.Token, trigger);
            loadTask = topicsLoadTask;
        }
        await loadTask.WaitAsync(cancellationToken);
    }

    private async Task LoadTopicsWithRetryAsync(bool logTopicLoad, int maxRetries, int version, CancellationToken token, string trigger)
    {
        if (logTopicLoad) appLogService?.LogInfo($"Loading topics for {Name}", "Topics");
        try
        {
            for (var attempt = 0; attempt <= maxRetries; attempt++)
            {
                token.ThrowIfCancellationRequested();
                var duration = await LoadTopicsCoreAsync(logTopicLoad, attempt == maxRetries, version, token);
                token.ThrowIfCancellationRequested();
                if (version != ConnectionVersion) return;
                var loaded = TopicLoadState == TopicLoadState.Loaded;
                if (loaded || attempt == maxRetries)
                {
                    if (telemetry.IsEnabled)
                        telemetry.Track(TelemetrySchema.Events.ClusterConnected, new Dictionary<string, object?>
                        {
                            ["client_kind"] = TelemetryExtensions.ClientKind(Client),
                            ["result"] = loaded ? "ok" : "failed",
                            ["topics"] = TelemetryBuckets.Count(Topics.Count),
                            ["duration_ms"] = duration,
                            ["attempts"] = attempt + 1,
                            ["trigger"] = trigger,
                        });
                    return;
                }
                await Task.Delay(TopicLoadRetryDelay, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private async Task<long?> LoadTopicsCoreAsync(bool logTopicLoad, bool isLastAttempt, int version, CancellationToken token)
    {
        long? started = null;
        long? duration = null;
        try
        {
            await OnUiAsync(() =>
            {
                TopicLoadState = TopicLoadState.Loading;
                SetCheckingIfStatusIsUnknown();
            });
            started = Stopwatch.GetTimestamp();
            var topics = await Client.GetTopicsAsync(cluster.Id).WaitAsync(token);
            duration = TelemetryBuckets.Milliseconds(Stopwatch.GetElapsedTime(started.Value));
            await OnUiAsync(() =>
            {
                if (!IsAvailable || version != ConnectionVersion) return;
                Topics.Clear();
                foreach (var topic in topics) Topics.Add(topic);
                topicsError = null;
                brokerStatus = ConnectionState.Connected;
                brokerError = null;
                TopicLoadState = TopicLoadState.Loaded;
                UpdateEffectiveStatus();
            });
            if (logTopicLoad) appLogService?.LogInfo($"Loaded {Topics.Count} topics for {Name}", "Topics");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception e)
        {
            if (duration == null && started is long timestamp)
                duration = TelemetryBuckets.Milliseconds(Stopwatch.GetElapsedTime(timestamp));
            Serilog.Log.Error(e, "Failed to load topics for cluster {ClusterName}", Name);
            await OnUiAsync(() =>
            {
                if (!IsAvailable || version != ConnectionVersion) return;
                topicsError = e.Message;
                TopicLoadState = TopicLoadState.Failed;
                UpdateEffectiveStatus();
            });
            if (logTopicLoad && isLastAttempt)
                appLogService?.LogError($"Could not load topics for {Name}: {e.Message}", "Topics");
        }
        return duration;
    }

    private void SetCheckingIfStatusIsUnknown()
    {
        if (Status == ConnectionState.Unknown) ApplyDiagnosticStatus(ConnectionState.Checking);
    }

    private static async Task OnUiAsync(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess()) action();
        else await Dispatcher.UIThread.InvokeAsync(action);
    }
}
