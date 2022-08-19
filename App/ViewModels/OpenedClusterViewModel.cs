using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KafkaLens.Core.Services;
using KafkaLens.Shared.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;

namespace KafkaLens.App.ViewModels
{
    public sealed class OpenedClusterViewModel : ObservableRecipient
    {
        private readonly ISettingsService settingsService;
        private readonly IClusterService clusterService;
        private readonly ClusterViewModel clusterViewModel;

        public string Name => clusterViewModel.Name;
        public ObservableCollection<Topic> Topics => clusterViewModel.Topics;
        
        public Topic? selectedTopic;

        public OpenedClusterViewModel(
            ISettingsService settingsService, 
            IClusterService clusterService,
            ClusterViewModel clusterViewModel)
        {            
            this.settingsService = settingsService;
            this.clusterService = clusterService;
            this.clusterViewModel = clusterViewModel;

            //var selectedTopicName = settingsService.GetValue<string>(nameof(SelectedTopic));
        }

        public Topic? SelectedTopic
        {
            get => selectedTopic;
            set
            {
                SetProperty(ref selectedTopic, value, true);

                settingsService.SetValue(nameof(SelectedTopic), value);

                // load messages
            }
        }
    }
}
