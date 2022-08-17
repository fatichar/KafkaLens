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
        private readonly ISettingsService settingsService;
        private readonly IClusterService clusterService;

        public IRelayCommand LoadClustersCommand { get; }
        public ObservableCollection<KafkaCluster> Clusters { get; } = new();
        
        public KafkaCluster? selectedCluster;

        public ClustersViewModel(ISettingsService settingsService, IClusterService clusterService)
        {
            LoadClustersCommand = new RelayCommand(LoadClustersAsync);
            this.settingsService = settingsService;
            this.clusterService = clusterService;
            var selectedClusterName = settingsService.GetValue<string>(nameof(SelectedCluster));            
        }

        private void LoadClustersAsync()
        {
            var clusters = clusterService.GetAllClusters();
            Clusters.Clear();
            foreach (var cluster in clusters)
            {
                Clusters.Add(cluster);
            }
        }

        public KafkaCluster? SelectedCluster
        {
            get => selectedCluster;
            set
            {
                SetProperty(ref selectedCluster, value, true);

                settingsService.SetValue(nameof(SelectedCluster), value);
            }
        }
    }
}
