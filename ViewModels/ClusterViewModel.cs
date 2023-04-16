using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using KafkaLens.Shared.Models;
using KafkaLens.Shared;

namespace KafkaLens.ViewModels;

public sealed class ClusterViewModel: ViewModelBase
{
    public IKafkaLensClient KafkaLensClient { get; }
    public IAsyncRelayCommand LoadTopicsCommand { get; }
    private readonly KafkaCluster cluster;
    public ObservableCollection<Topic> Topics { get; } = new();

    public string Id => cluster.Id;
    public string Name => cluster.Name;
    public string Address => cluster.Address;

    public ClusterViewModel(KafkaCluster cluster, IKafkaLensClient kafkaLensClient)
    {
        KafkaLensClient = kafkaLensClient;
        this.cluster = cluster;
        IsConnected = this.cluster.IsConnected;

        LoadTopicsCommand = new AsyncRelayCommand(LoadTopicsAsync);
    }

    public bool IsConnected { get; set; }

    private async Task LoadTopicsAsync()
    {
        Topics.Clear();
        var topics = await KafkaLensClient.GetTopicsAsync(cluster.Id);
        foreach (var topic in topics)
        {
            Topics.Add(topic);
        }
    }
}