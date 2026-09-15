using System.Collections.ObjectModel;
using System.Threading;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KafkaLens.Shared.Models;
using KafkaLens.Shared;
using KafkaLens.ViewModels.Services;

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

    public ClusterViewModel(KafkaCluster cluster, IKafkaLensClient client, IAppLogService? appLogService = null)
    {
        Client = client;
        this.cluster = cluster;
        this.appLogService = appLogService;
        name = cluster.Name;
        address = cluster.Address;
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

    public async Task CheckConnectionAsync(bool allowRetries = true, CancellationToken cancellationToken = default)
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
            if (isConnected && topicsError != null)
                await EnsureTopicsLoadedAsync(forceRefresh: true, logTopicLoad: true,
                    maxRetries: allowRetries ? TOPIC_LOAD_RETRY_COUNT : 0, cancellationToken: token);
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
        await CheckConnectionAsync();
        if (brokerStatus == ConnectionState.Connected)
            await EnsureTopicsLoadedAsync(forceRefresh: true, logTopicLoad: true);
    }

    private readonly object topicsLoadLock = new();
    private Task? topicsLoadTask;

    private async Task LoadTopicsAsync()
    {
        await EnsureTopicsLoadedAsync(forceRefresh: true, logTopicLoad: true);
    }

    internal async Task EnsureTopicsLoadedAsync(bool forceRefresh = false, bool logTopicLoad = false,
        int maxRetries = TOPIC_LOAD_RETRY_COUNT, CancellationToken cancellationToken = default)
    {
        if (!IsAvailable || IsUnavailablePlaceholder) return;
        Task loadTask;
        lock (topicsLoadLock)
        {
            if (!forceRefresh && TopicLoadState == TopicLoadState.Loaded) return;
            if (topicsLoadTask == null || topicsLoadTask.IsCompleted)
                topicsLoadTask = LoadTopicsWithRetryAsync(logTopicLoad, maxRetries, ConnectionVersion, connectionLifetime.Token);
            loadTask = topicsLoadTask;
        }
        await loadTask.WaitAsync(cancellationToken);
    }

    private async Task LoadTopicsWithRetryAsync(bool logTopicLoad, int maxRetries, int version, CancellationToken token)
    {
        if (logTopicLoad) appLogService?.LogInfo($"Loading topics for {Name}", "Topics");
        try
        {
            for (var attempt = 0; attempt <= maxRetries; attempt++)
            {
                token.ThrowIfCancellationRequested();
                await LoadTopicsCoreAsync(logTopicLoad, attempt == maxRetries, version, token);
                if (TopicLoadState == TopicLoadState.Loaded) return;
                if (attempt < maxRetries) await Task.Delay(TopicLoadRetryDelay, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private async Task LoadTopicsCoreAsync(bool logTopicLoad, bool isLastAttempt, int version, CancellationToken token)
    {
        try
        {
            await OnUiAsync(() =>
            {
                TopicLoadState = TopicLoadState.Loading;
                SetCheckingIfStatusIsUnknown();
            });
            var topics = await Client.GetTopicsAsync(cluster.Id).WaitAsync(token);
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
