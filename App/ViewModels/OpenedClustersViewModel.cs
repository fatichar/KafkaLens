using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging.Messages;
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
    public sealed class OpenedClustersViewModel : ObservableRecipient
    {
        private readonly ISettingsService settingsService;
        private readonly IClusterService clusterService;

        public IRelayCommand LoadClustersCommand { get; }
        public ObservableCollection<OpenedClusterViewModel> Clusters { get; } = new();
        
        public OpenedClusterViewModel? selectedCluster;

        public OpenedClustersViewModel(ISettingsService settingsService, IClusterService clusterService)
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
                //Clusters.Add(cluster);
            }
        }

        public OpenedClusterViewModel? SelectedCluster
        {
            get => selectedCluster;
            set
            {
                SetProperty(ref selectedCluster, value, true);

                settingsService.SetValue(nameof(SelectedCluster), value);
            }
        }

        //public void Receive(PropertyChangedMessage<KafkaCluster> message)
        //{
        //    if (message.Sender.GetType() == typeof(OpenedClusterViewModel) &&
        //            message.PropertyName == nameof(OpenedClusterViewModel.SelectedTopic))
        //    {
        //        TopicPartition = (TopicPartition?)message.NewValue;

        //        LoadMessagesAsync();
        //    }
        //}
    }
}
