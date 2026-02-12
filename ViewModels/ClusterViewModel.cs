using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
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
    public string Name => cluster.Name;
    public string Address => cluster.Address;

    public ClusterViewModel(KafkaCluster cluster, IKafkaLensClient client)
    {
        Client = client;
        this.cluster = cluster;
        IsConnected = this.cluster.IsConnected;

        LoadTopicsCommand = new AsyncRelayCommand(LoadTopicsAsync);
    }

    public async Task CheckConnectionAsync()
    {
        IsConnected = await Client.ValidateConnectionAsync(Address);
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