using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KafkaLens.Shared.Models;
using KafkaLens.Shared;

namespace KafkaLens.ViewModels;

public sealed partial class ClusterViewModel: ConnectionViewModelBase
{
    public IKafkaLensClient Client { get; }
    public IAsyncRelayCommand LoadTopicsCommand { get; }
    private readonly KafkaCluster cluster;
    public ObservableCollection<Topic> Topics { get; } = new();

    public string Id => cluster.Id;

    [ObservableProperty]
    private string name;

    [ObservableProperty]
    private string address;

    public ClusterViewModel(KafkaCluster cluster, IKafkaLensClient client)
    {
        Client = client;
        this.cluster = cluster;
        name = cluster.Name;
        address = cluster.Address;
        Status = this.cluster.Status;
        LastError = this.cluster.LastError;

        LoadTopicsCommand = new AsyncRelayCommand(LoadTopicsAsync);
    }

    public async Task CheckConnectionAsync(bool eagerLoadTopics = false)
    {
        Status = ConnectionState.Checking;
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
                Status = isConnected ? ConnectionState.Connected : ConnectionState.Failed;
            }
            catch (Exception e)
            {
                LastError = e.Message;
                Status = ConnectionState.Failed;
            }
        }
    }

    private bool isLoadingTopics;
    private async Task LoadTopicsAsync()
    {
        if (isLoadingTopics) return;
        isLoadingTopics = true;
        try
        {
            Status = ConnectionState.Checking;
            Topics.Clear();
            var topics = await Client.GetTopicsAsync(cluster.Id);
            foreach (var topic in topics)
            {
                Topics.Add(topic);
            }
            Status = ConnectionState.Connected;
        }
        catch (Exception e)
        {
            Serilog.Log.Error(e, "Failed to load topics for cluster {ClusterName}", Name);
            LastError = e.Message;
            Status = ConnectionState.Failed;
        }
        finally
        {
            isLoadingTopics = false;
        }
    }
}