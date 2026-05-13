using System.Collections.ObjectModel;
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

    public IKafkaLensClient Client { get; }
    public IAsyncRelayCommand LoadTopicsCommand { get; }
    private readonly KafkaCluster cluster;
    private readonly IAppLogService? appLogService;
    public ObservableCollection<Topic> Topics { get; } = new();

    public string Id => cluster.Id;

    [ObservableProperty]
    private string name;

    [ObservableProperty]
    private string address;

    [ObservableProperty]
    private TopicLoadState topicLoadState = TopicLoadState.NotLoaded;

    public ClusterViewModel(KafkaCluster cluster, IKafkaLensClient client, IAppLogService? appLogService = null)
    {
        Client = client;
        this.cluster = cluster;
        this.appLogService = appLogService;
        name = cluster.Name;
        address = cluster.Address;
        Status = this.cluster.Status;
        LastError = this.cluster.LastError;

        LoadTopicsCommand = new AsyncRelayCommand(LoadTopicsAsync);
    }

    public async Task CheckConnectionAsync()
    {
        await CheckConnectionAsync(logTopicLoad: true);
    }

    private async Task CheckConnectionAsync(bool logTopicLoad)
    {
        SetCheckingIfStatusIsUnknown();
        appLogService?.LogInfo($"Connecting to {Name}", "Connection");
        try
        {
            var isConnected = await Client.ValidateConnectionAsync(Address);
            if (isConnected)
            {
                LastError = null;
                Status = ConnectionState.Connected;
                appLogService?.LogInfo($"Connected to {Name}", "Connection");
            }
            else
            {
                await ConfirmConnectionByLoadingTopicsAsync(logTopicLoad);
            }
        }
        catch (Exception e)
        {
            LastError = e.Message;
            Status = ConnectionState.Failed;
            appLogService?.LogError($"Could not connect to {Name}: {e.Message}", "Connection");
        }
    }

    private async Task ConfirmConnectionByLoadingTopicsAsync(bool logTopicLoad)
    {
        try
        {
            await EnsureTopicsLoadedAsync(forceRefresh: true, logTopicLoad: logTopicLoad);
        }
        catch (Exception e)
        {
            LastError = e.Message;
            Status = ConnectionState.Failed;
        }

        if (Status == ConnectionState.Connected)
        {
            LastError = null;
            appLogService?.LogInfo($"Connected to {Name}", "Connection");
        }
        else
        {
            appLogService?.LogWarning($"Could not connect to {Name}", "Connection");
        }
    }

    private readonly object topicsLoadLock = new();
    private Task? topicsLoadTask;

    private async Task LoadTopicsAsync()
    {
        await EnsureTopicsLoadedAsync(forceRefresh: true, logTopicLoad: true);
    }

    internal async Task EnsureTopicsLoadedAsync(bool forceRefresh = false, bool logTopicLoad = false)
    {
        Task loadTask;
        lock (topicsLoadLock)
        {
            if (!forceRefresh && TopicLoadState == TopicLoadState.Loaded)
            {
                return;
            }

            if (topicsLoadTask == null || topicsLoadTask.IsCompleted)
            {
                topicsLoadTask = LoadTopicsWithRetryAsync(logTopicLoad);
            }

            loadTask = topicsLoadTask;
        }

        await loadTask;
    }

    private async Task LoadTopicsWithRetryAsync(bool logTopicLoad)
    {
        for (var attempt = 0; attempt <= TOPIC_LOAD_RETRY_COUNT; attempt++)
        {
            await LoadTopicsCoreAsync(logTopicLoad);
            if (TopicLoadState == TopicLoadState.Loaded)
            {
                return;
            }

            if (attempt < TOPIC_LOAD_RETRY_COUNT)
            {
                await Task.Delay(TopicLoadRetryDelay);
            }
        }
    }

    private async Task LoadTopicsCoreAsync(bool logTopicLoad)
    {
        try
        {
            TopicLoadState = TopicLoadState.Loading;
            SetCheckingIfStatusIsUnknown();
            if (logTopicLoad)
                appLogService?.LogInfo($"Loading topics for {Name}", "Topics");
            var topics = await Client.GetTopicsAsync(cluster.Id);
            Topics.Clear();
            foreach (var topic in topics)
            {
                Topics.Add(topic);
            }
            Status = ConnectionState.Connected;
            LastError = null;
            TopicLoadState = TopicLoadState.Loaded;
            if (logTopicLoad)
                appLogService?.LogInfo($"Loaded {Topics.Count} topics for {Name}", "Topics");
        }
        catch (Exception e)
        {
            Serilog.Log.Error(e, "Failed to load topics for cluster {ClusterName}", Name);
            LastError = e.Message;
            Status = ConnectionState.Failed;
            TopicLoadState = TopicLoadState.Failed;
            if (logTopicLoad)
                appLogService?.LogError($"Could not load topics for {Name}: {e.Message}", "Topics");
        }
    }

    private void SetCheckingIfStatusIsUnknown()
    {
        if (Status == ConnectionState.Unknown)
            Status = ConnectionState.Checking;
    }
}
