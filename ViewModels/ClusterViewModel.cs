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
        IsConnected = this.cluster.IsConnected;

        LoadTopicsCommand = new AsyncRelayCommand(LoadTopicsAsync);
    }

    public async Task CheckConnectionAsync(bool eagerLoadTopics = false)
    {
        if (eagerLoadTopics)
        {
            // By loading topics, we implicitly validate the connection
            // AND cache the topics for instantaneous UI loading.
            await LoadTopicsAsync();
        }
        else
        {
            IsConnected = await Client.ValidateConnectionAsync(Address);
        }
    }

    private async Task LoadTopicsAsync()
    {
        try
        {
            Topics.Clear();
            var topics = await Client.GetTopicsAsync(cluster.Id);
            foreach (var topic in topics)
            {
                Topics.Add(topic);
            }
            IsConnected = true;
        }
        catch (Exception e)
        {
            Serilog.Log.Error(e, "Failed to load topics for cluster {ClusterName}", Name);
            IsConnected = false;
        }
    }
}