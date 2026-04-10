using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using KafkaLens.ViewModels;

namespace AvaloniaApp.Views;

public partial class MainView : UserControl
{
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
}
