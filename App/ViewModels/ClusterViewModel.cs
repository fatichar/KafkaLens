using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KafkaLens.Core.Services;
using KafkaLens.Shared.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KafkaLens.App.ViewModels
{
    public sealed class ClusterViewModel : ObservableRecipient
    {
        private readonly IClusterService clusterService;
        public IRelayCommand OpenClusterCommand { get; }
        public IAsyncRelayCommand LoadTopicsCommand { get; }
        private readonly KafkaCluster cluster;
        public ObservableCollection<Topic> Topics { get; } = new();

        public string Name => cluster.Name;

        public ClusterViewModel(KafkaCluster cluster, IClusterService clusterService)
        {
            this.clusterService = clusterService;
            this.cluster = cluster;

            OpenClusterCommand = new RelayCommand(OpenClusterAsync);
            LoadTopicsCommand = new AsyncRelayCommand(LoadTopicsAsync);
        }

        private async void OpenClusterAsync()
        {
            OpenedCluster = cluster;
            if (Topics.Count == 0)
            {
                await LoadTopicsAsync();
            }
        }

        public KafkaCluster OpenedCluster { get; set; }

        private async Task LoadTopicsAsync()
        {
            var topics = await clusterService.GetTopicsAsync(cluster.Id);
            foreach (var topic in topics)
            {
                Topics.Add(topic);
            }
        }
    }
}
