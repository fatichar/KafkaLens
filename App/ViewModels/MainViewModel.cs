using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using KafkaLens.App.Messages;
using KafkaLens.Core.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KafkaLens.App.ViewModels
{
    public class MainViewModel : ObservableRecipient
    {
        // data
        public ObservableCollection<ClusterViewModel> Clusters { get; } = new();
        public ObservableCollection<OpenedClusterViewModel> OpenedClusters { get; } = new();

        public OpenedClusterViewModel? selectedCluster;

        // services
        private readonly ISettingsService settingsService;
        private readonly IClusterService clusterService;

        // commands
        public IRelayCommand AddClusterCommand { get; }
        public IRelayCommand LoadClustersCommand { get; }

        public MainViewModel(ISettingsService settingsService, IClusterService clusterService)
        {
            this.settingsService = settingsService;
            this.clusterService = clusterService;
            
            AddClusterCommand = new RelayCommand(AddClusterAsync);
            LoadClustersCommand = new RelayCommand(LoadClustersAsync);

            IsActive = true;
        }

        protected override void OnActivated()
        {
            Messenger.Register<MainViewModel, OpenClusterMessage>(this, (r, m) => r.Receive(m));
        }

        public OpenedClusterViewModel? SelectedCluster
        {
            get => selectedCluster;
            set
            {
                SetProperty(ref selectedCluster, value, true);

                settingsService.SetValue(nameof(SelectedCluster), value.ClusterId);
            }
        }

        private void AddClusterAsync()
        {
        }

        public void Receive(OpenClusterMessage message)
        {
            var cluster = new OpenedClusterViewModel(settingsService, clusterService, message.ClusterViewModel);
            OpenedClusters.Add(cluster);
            selectedIndex = OpenedClusters.Count - 1;
        }

        private void LoadClustersAsync()
        {
            var clusters = clusterService.GetAllClusters();
            Clusters.Clear();
            foreach (var cluster in clusters)
            {
                Clusters.Add(new ClusterViewModel(cluster, clusterService));
            }
            selectedIndex = 0;
        }

        private int selectedIndex = -1;

        public int SelectedIndex { get => selectedIndex; set => SetProperty(ref selectedIndex, value); }
    }
}
