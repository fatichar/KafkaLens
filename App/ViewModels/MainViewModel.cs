﻿using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using KafkaLens.App.Messages;
using KafkaLens.Core.Services;
using System.Collections.ObjectModel;
using System.Linq;

namespace KafkaLens.App.ViewModels
{
    public class MainViewModel : ObservableRecipient
    {
        // data
        public ObservableCollection<ClusterViewModel> Clusters { get; } = new();
        public ObservableCollection<OpenedClusterViewModel> OpenedClusters { get; } = new();
        private IDictionary<string, IList<OpenedClusterViewModel>> openedClustersMap = new Dictionary<string, IList<OpenedClusterViewModel>>();

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
            OpenCluster(message.ClusterViewModel);
            SelectedIndex = OpenedClusters.Count - 1;
        }

        private void OpenCluster(ClusterViewModel clusterViewModel)
        {
            string newName = clusterViewModel.Name;
            if (!openedClustersMap.TryGetValue(clusterViewModel.Id, out var alreadyOpened))
            {
                alreadyOpened = new List<OpenedClusterViewModel>();
                openedClustersMap.Add(clusterViewModel.Id, alreadyOpened);
            }
            else
            {
                // cluster already opened, so generate new name
                newName = GenerateNewName(clusterViewModel.Name, alreadyOpened);
            }
            var openedCluster = new OpenedClusterViewModel(settingsService, clusterService, clusterViewModel, newName);
            alreadyOpened.Add(openedCluster);
            OpenedClusters.Add(openedCluster);
            openedCluster.LoadTopicsAsync();
        }

        private string GenerateNewName(string clusterName, IList<OpenedClusterViewModel> alreadyOpened)
        {
            var existingNames = alreadyOpened.Select(c => c.Name).ToList();
            var suffixes = existingNames.ConvertAll(n => n.Length > clusterName.Length + 1 ? n.Substring(clusterName.Length + 1) : "");
            suffixes.Remove("");
            var numbersStrings = suffixes.ConvertAll(s => s.Length > 1 ? s.Substring(1, s.Length - 2) : "");
            var numbers = numbersStrings.ConvertAll(ns => int.TryParse(ns, out var number) ? number : 0);
            numbers.Sort();
            var smallestAvalable = numbers.Count + 1;
            for (var i = 0; i < numbers.Count; i++)
            {
                if (numbers[i] > i + 1)
                {
                    smallestAvalable = i + 1;
                    break;
                }
            }
            return $"{clusterName} ({smallestAvalable})";
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
