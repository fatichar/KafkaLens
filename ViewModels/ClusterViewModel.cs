using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using KafkaLens.Shared.Models;
using KafkaLens.Messages;
using KafkaLens.Shared;

namespace KafkaLens.ViewModels;

public sealed class ClusterViewModel: ViewModelBase
{
    public IKafkaLensClient KafkaLensClient { get; }
    public IRelayCommand OpenClusterCommand { get; }
    public IAsyncRelayCommand LoadTopicsCommand { get; }
    private readonly KafkaCluster cluster;
    public ObservableCollection<Topic> Topics { get; } = new();

    public string Id => cluster.Id;
    public string Name => cluster.Name;
    public string Address => cluster.BootstrapServers;

    public ClusterViewModel(KafkaCluster cluster, IKafkaLensClient kafkaLensClient)
    {
        this.KafkaLensClient = kafkaLensClient;
        this.cluster = cluster;

        OpenClusterCommand = new RelayCommand(OpenCluster);
        LoadTopicsCommand = new AsyncRelayCommand(LoadTopicsAsync);
    }

    private void OpenCluster()
    {
        _ = Messenger.Send(new OpenClusterMessage(this));
    }

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