using System;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.VisualTree;
using KafkaLens.ViewModels;

namespace App.Views;

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
    }

    private void OnShowEditClustersDialog()
    {
        var mainWindow = GetMainWindow();
        var dialog = new EditClustersDialog();
        var dataContext = DataContext as MainViewModel;
        dialog.DataContext = new LocalClustersViewModel(dataContext.ClusterInfoRepository);
        dialog.ShowDialog(mainWindow).ContinueWith(_ => dataContext.LoadClusters());
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