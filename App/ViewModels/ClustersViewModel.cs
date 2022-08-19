using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KafkaLens.Core.Services;
using KafkaLens.Shared.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KafkaLens.App.ViewModels
{
    public sealed class ClustersViewModel : ObservableRecipient
    {
        // services
        private readonly ISettingsService settingsService;
        private readonly IClusterService clusterService;

        // commands
        public IRelayCommand AddClusterCommand { get; }
        public IRelayCommand LoadClustersCommand { get; }

        // data
        public ObservableCollection<ClusterViewModel> Clusters { get; } = new();
        
        public ClustersViewModel(ISettingsService settingsService, IClusterService clusterService)
        {
            AddClusterCommand = new RelayCommand(AddClusterAsync);
            LoadClustersCommand = new RelayCommand(LoadClustersAsync);

            this.settingsService = settingsService;
            this.clusterService = clusterService;
        }

        private void AddClusterAsync()
        {
            throw new NotImplementedException();
        }

        private void LoadClustersAsync()
        {
            var clusters = clusterService.GetAllClusters();
            Clusters.Clear();
            foreach (var cluster in clusters)
            {
                Clusters.Add(new ClusterViewModel(cluster, clusterService));
            }
        }
    }
}
