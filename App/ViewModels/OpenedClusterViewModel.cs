using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KafkaLens.Shared.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KafkaLens.App.ViewModels
{
    public sealed class OpenedClusterViewModel : ObservableRecipient
    {
        private readonly ISettingsService settingsService;
        public IAsyncRelayCommand LoadTopicsCommand { get; }
        public ObservableCollection<Topic> Topics { get; } = new();
        
        public Topic? selectedTopic;

        public OpenedClusterViewModel(ISettingsService settingsService)
        {
            LoadTopicsCommand = new AsyncRelayCommand(LoadTopicsAsync);
            this.settingsService = settingsService;

            var selectedTopicName = settingsService.GetValue<string>(nameof(SelectedTopic));            
        }

        private Task LoadTopicsAsync()
        {
            throw new NotImplementedException();
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
