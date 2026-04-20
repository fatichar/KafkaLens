using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KafkaLens.Shared.Models;
using KafkaLens.Shared;
using KafkaLens.ViewModels.Services;

namespace KafkaLens.ViewModels;

public sealed partial class ClusterViewModel: ConnectionViewModelBase
{
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

    public async Task CheckConnectionAsync(bool eagerLoadTopics = false)
    {
        SetCheckingIfStatusIsUnknown();
        appLogService?.LogInfo($"Connecting to {Name}", "Connection");
        if (eagerLoadTopics)
        {
            // By loading topics, we implicitly validate the connection
            // AND cache the topics for instantaneous UI loading.
            await LoadTopicsAsync();
        }
        else
        {
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
                    await ConfirmConnectionByLoadingTopicsAsync();
                }
            }
            catch (Exception e)
            {
                LastError = e.Message;
                Status = ConnectionState.Failed;
                appLogService?.LogError($"Could not connect to {Name}: {e.Message}", "Connection");
            }
        }
    }

    private async Task ConfirmConnectionByLoadingTopicsAsync()
    {
        try
        {
            await LoadTopicsAsync();
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

    private bool isLoadingTopics;
    private async Task LoadTopicsAsync()
    {
        if (isLoadingTopics) return;
        isLoadingTopics = true;
        try
        {
            SetCheckingIfStatusIsUnknown();
            appLogService?.LogInfo($"Loading topics for {Name}", "Topics");
            Topics.Clear();
            var topics = await Client.GetTopicsAsync(cluster.Id);
            foreach (var topic in topics)
            {
                Topics.Add(topic);
            }
            Status = ConnectionState.Connected;
            LastError = null;
            appLogService?.LogInfo($"Loaded {Topics.Count} topics for {Name}", "Topics");
        }
        catch (Exception e)
        {
            Serilog.Log.Error(e, "Failed to load topics for cluster {ClusterName}", Name);
            LastError = e.Message;
            Status = ConnectionState.Failed;
            appLogService?.LogError($"Could not load topics for {Name}: {e.Message}", "Topics");
        }
        finally
        {
            isLoadingTopics = false;
        }
    }

    private void SetCheckingIfStatusIsUnknown()
    {
        if (Status == ConnectionState.Unknown)
            Status = ConnectionState.Checking;
    }
}
