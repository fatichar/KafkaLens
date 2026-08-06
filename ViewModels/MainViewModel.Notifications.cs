using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using KafkaLens.ViewModels.Messages;

namespace KafkaLens.ViewModels;

public partial class MainViewModel
{
    private bool saveNotificationsHiddenForSession;
    private string? saveNotificationDirectory;

    [ObservableProperty]
    private bool isSaveNotificationVisible;

    [ObservableProperty]
    private string saveNotificationMessage = "";

    public IRelayCommand OpenSavedMessagesDirectoryCommand { get; private set; } = null!;
    public IRelayCommand CloseSaveNotificationCommand { get; private set; } = null!;
    public IRelayCommand HideSaveNotificationsForSessionCommand { get; private set; } = null!;
    public IRelayCommand NeverShowSaveNotificationsCommand { get; private set; } = null!;

    private void InitializeSaveNotifications()
    {
        OpenSavedMessagesDirectoryCommand = new RelayCommand(OpenSavedMessagesDirectory);
        CloseSaveNotificationCommand = new RelayCommand(() => IsSaveNotificationVisible = false);
        HideSaveNotificationsForSessionCommand = new RelayCommand(HideSaveNotificationsForSession);
        NeverShowSaveNotificationsCommand = new RelayCommand(NeverShowSaveNotifications);

        WeakReferenceMessenger.Default.Register<MessagesSavedMessage>(this, (_, message) =>
        {
            Dispatcher.UIThread.Post(() => ShowSaveNotification(message));
        });
    }

    private void ShowSaveNotification(MessagesSavedMessage message)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS() && !OperatingSystem.IsLinux())
            return;

        if (saveNotificationsHiddenForSession || !settingsService.GetBrowserConfig().ShowSaveNotification)
            return;

        saveNotificationDirectory = message.Directory;
        SaveNotificationMessage = $"Saved {message.Count} {(message.Count == 1 ? "message" : "messages")} to {message.Directory}";
        IsSaveNotificationVisible = true;
    }

    private void OpenSavedMessagesDirectory()
    {
        if (!string.IsNullOrWhiteSpace(saveNotificationDirectory))
            OpenExternalFolder(saveNotificationDirectory);
    }

    private void HideSaveNotificationsForSession()
    {
        saveNotificationsHiddenForSession = true;
        IsSaveNotificationVisible = false;
    }

    private void NeverShowSaveNotifications()
    {
        var config = settingsService.GetBrowserConfig();
        config.ShowSaveNotification = false;
        settingsService.SaveBrowserConfig(config);
        IsSaveNotificationVisible = false;
    }
}
