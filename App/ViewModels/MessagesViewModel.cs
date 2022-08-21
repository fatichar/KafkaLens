using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging.Messages;
using CommunityToolkit.Mvvm.Messaging;
using KafkaLens.Shared.Models;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;

namespace KafkaLens.App.ViewModels
{
    public sealed class MessagesViewModel : ObservableRecipient
    {
        private MessageViewModel? currentMessage;
        public IAsyncRelayCommand LoadMessagesCommand { get; }

        public ObservableCollection<MessageViewModel> Messages { get; internal set; }

        public ObservableCollection<MessageViewModel> SelectedMessages { get; internal set; }

        public MessagesViewModel()
        {
            Messages = new();
            SelectedMessages = new();
            LoadMessagesCommand = new AsyncRelayCommand(LoadMessagesAsync);
        }

        private Task LoadMessagesAsync()
        {
            throw new NotImplementedException();
        }

        public MessageViewModel? CurrentMessage
        {
            get => currentMessage;
            set => SetProperty(ref currentMessage, value);
        }
    }
}
