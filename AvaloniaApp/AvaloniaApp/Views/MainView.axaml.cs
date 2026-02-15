using Avalonia.Controls;
using Avalonia.VisualTree;
using KafkaLens.ViewModels;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

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
            var box = MessageBoxManager.GetMessageBoxStandard(title, message, ButtonEnum.Ok);
            box.ShowAsync();
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
        return this.GetVisualRoot() as Window;
    }

    private async void OnShowFolderOpenDialog()
    {
        var mainWindow = GetMainWindow();
        var dialog = new OpenFolderDialog
        {
            Title = "Select a folder"
        };
        if (mainWindow == null) return;
        var result = dialog.ShowAsync(mainWindow);
        var path = await result;
        if (path == null)
        {
            return;
        }

        var dataContext = DataContext as MainViewModel;
        dataContext?.OpenSavedMessages(path);
    }
}