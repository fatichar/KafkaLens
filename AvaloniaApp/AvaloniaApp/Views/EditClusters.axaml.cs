using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using KafkaLens.Clients.Entities;
using KafkaLens.Shared.Entities;
using KafkaLens.ViewModels;

namespace AvaloniaApp.Views;

public partial class EditClustersDialog : Window
{
    private string fileExplorerCommand = "";
    private string AppDataPath { get; set; }

    public EditClustersDialog()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        AppDataPath = Path.Combine(appDataPath, "KafkaLens");

        InitPlatform();

        InitializeComponent();
    }

    private EditClustersViewModel Context => DataContext as EditClustersViewModel;

    #region Direct Clusters
    private async void AddCluster_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new AddEditClusterDialog();
        var result = await dialog.ShowDialog<ClusterInfo?>(this);

        if (result != null)
        {
            Context.AddCluster(result.Name, result.Address);
        }
    }

    private async void EditCluster_Click(object? sender, RoutedEventArgs e)
    {
        var selected = ClustersGrid.SelectedItem as ClusterInfo;
        if (selected == null) return;

        var dialog = new AddEditClusterDialog(selected);
        var result = await dialog.ShowDialog<ClusterInfo?>(this);

        if (result != null)
        {
            Context.UpdateCluster(result);
        }
    }

    private void RemoveCluster_Click(object? sender, RoutedEventArgs e)
    {
        var selected = ClustersGrid.SelectedItem as ClusterInfo;
        Context.RemoveCluster(selected);
    }
    #endregion

    #region KafkaLens Clients
    private async void AddClient_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new AddEditClientDialog();
        var result = await dialog.ShowDialog<ClientInfo?>(this);

        if (result != null)
        {
            Context.AddClient(result.Name, result.Address, result.Protocol);
        }
    }

    private async void EditClient_Click(object? sender, RoutedEventArgs e)
    {
        var selected = ClientsGrid.SelectedItem as ClientInfo;
        if (selected == null) return;

        var dialog = new AddEditClientDialog(selected);
        var result = await dialog.ShowDialog<ClientInfo?>(this);

        if (result != null)
        {
            Context.UpdateClient(result);
        }
    }

    private void RemoveClient_Click(object? sender, RoutedEventArgs e)
    {
        var selected = ClientsGrid.SelectedItem as ClientInfo;
        Context.RemoveClient(selected);
    }
    #endregion

    private void OpenSettingsButton_OnClick(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(fileExplorerCommand))
        {
            Process.Start(fileExplorerCommand, "\"" + AppDataPath + "\"");
        }
    }

    private void InitPlatform()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            fileExplorerCommand = "explorer.exe";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            fileExplorerCommand = "open";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            fileExplorerCommand = "xdg-open";
        }
    }
}