using System;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaApp.Services;
using AvaloniaApp.Utils;
using KafkaLens.ViewModels;

namespace AvaloniaApp.Views;

public partial class MainView : UserControl
{
    private MainViewModel? subscribedViewModel;

    public MainView()
    {
        InitializeComponent();

        MainViewModel.ShowAboutDialog += () =>
        {
            var mainWindow = GetMainWindow();
            if (mainWindow != null)
            {
                var about = new About();
                about.ShowDialog(mainWindow);
            }
        };

        MainViewModel.ShowFolderOpenDialog += OnShowFolderOpenDialog;
        MainViewModel.OpenDiagnosticLogFile += OnOpenDiagnosticLogFile;

        MainViewModel.ShowEditClustersDialog += OnShowEditClustersDialog;

        MainViewModel.ShowUpdateDialog += (updateVm) =>
        {
            var mainWindow = GetMainWindow();
            if (mainWindow != null)
            {
                var dialog = new UpdateDialog { DataContext = updateVm };
                dialog.ShowDialog(mainWindow);
            }
        };

        MainViewModel.ShowMessage += (title, message) =>
        {
            var mainWindow = GetMainWindow();
            if (mainWindow == null)
            {
                return;
            }

            var box = new SimpleMessageBox(title, message, isConfirmation: false);
            _ = box.ShowMessageAsync(mainWindow);
        };

        MainViewModel.ConfirmRestoreTabs = async (count) =>
        {
            var mainWindow = GetMainWindow();
            if (mainWindow == null)
            {
                return false;
            }

            var box = new SimpleMessageBox(
                "Restore Tabs",
                $"You had {count} {(count > 1 ? "tabs" : "tab")} open in the previous session, restore now?",
                isConfirmation: true);

            return await box.ShowConfirmationAsync(mainWindow);
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (subscribedViewModel != null)
        {
            subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            subscribedViewModel.AppLogService.Entries.CollectionChanged -= OnAppLogEntriesChanged;
        }

        base.OnDataContextChanged(e);

        subscribedViewModel = DataContext as MainViewModel;
        if (subscribedViewModel != null)
        {
            subscribedViewModel.PropertyChanged += OnViewModelPropertyChanged;
            subscribedViewModel.AppLogService.Entries.CollectionChanged += OnAppLogEntriesChanged;
        }
    }

    private void OnShowEditClustersDialog()
    {
        var mainWindow = GetMainWindow();
        var dialog = new EditClustersDialog();
        var dataContext = DataContext as MainViewModel;
        if (dataContext == null || mainWindow == null) return;
        dialog.DataContext = new EditClustersViewModel(dataContext.Clusters, dataContext.ClusterInfoRepository, dataContext.ClientInfoRepository, dataContext.ClientFactory);
        dialog.ShowDialog(mainWindow);
    }

    private Window? GetMainWindow()
    {
        return TopLevel.GetTopLevel(this) as Window;
    }

    private async void OnShowFolderOpenDialog()
    {
        var mainWindow = GetMainWindow();
        if (mainWindow == null) return;

        var folders = await mainWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select a folder",
            AllowMultiple = false
        });

        if (folders.Count == 0)
        {
            return;
        }

        var path = folders[0].Path.LocalPath;
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var dataContext = DataContext as MainViewModel;
        dataContext?.OpenSavedMessages(path);
    }

    private void OnOpenDiagnosticLogFile()
    {
        var locator = new SerilogLogFileLocator(App.GetLogPath());
        var logFile = locator.FindLatestLogFile();
        if (string.IsNullOrWhiteSpace(logFile))
        {
            MainViewModel.ShowMessage("Log File", "No diagnostic log file was found yet.");
            return;
        }

        if (!OsUtils.OpenExternal(logFile))
            MainViewModel.ShowMessage("Log File", "Could not open the diagnostic log file.");
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsAppLogPanelVisible) &&
            subscribedViewModel?.IsAppLogPanelVisible == true)
        {
            ScrollAppLogToBottom();
        }
    }

    private void OnAppLogEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (subscribedViewModel?.IsAppLogPanelVisible == true && AppLogGrid.SelectedItem == null)
            ScrollAppLogToBottom();
    }

    private void ScrollAppLogToBottom()
    {
        var entries = subscribedViewModel?.AppLogService.Entries;
        if (entries is not { Count: > 0 })
            return;

        var lastEntry = entries[^1];
        Dispatcher.UIThread.Post(() => AppLogGrid.ScrollIntoView(lastEntry, null));
    }
}
