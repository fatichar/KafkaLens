using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using KafkaLens.Core.Services;
using KafkaLens.Shared.Models;
using KafkaLens.Messages;

namespace KafkaLens.ViewModels;

public sealed class ClusterViewModel : ObservableRecipient
{
    private readonly IClusterService clusterService;
    public IRelayCommand OpenClusterCommand { get; }
    public IAsyncRelayCommand LoadTopicsCommand { get; }
    private readonly KafkaCluster cluster;
    public ObservableCollection<Topic> Topics { get; } = new();

    public string Id => cluster.Id;
    public string Name => cluster.Name;
    public string Address => cluster.BootstrapServers;

    public ClusterViewModel(KafkaCluster cluster, IClusterService clusterService)
    {
        this.clusterService = clusterService;
        this.cluster = cluster;

        OpenClusterCommand = new RelayCommand(OpenClusterAsync);
        LoadTopicsCommand = new AsyncRelayCommand(LoadTopicsAsync);
    }

    private async void OpenClusterAsync()
    {
        _ = Messenger.Send(new OpenClusterMessage(this));
    }

    private async Task LoadTopicsAsync()
    {
        Topics.Clear();
        var topics = clusterService.GetTopics(cluster.Id);
        foreach (var topic in topics)
        {
            Topics.Add(topic);
        }
    }
}