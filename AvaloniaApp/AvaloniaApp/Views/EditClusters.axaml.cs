using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using KafkaLens.Clients.Entities;
using KafkaLens.Shared.Entities;
using KafkaLens.ViewModels;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

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

    private EditClustersViewModel? Context => DataContext as EditClustersViewModel;

    #region Direct Clusters
    private async void AddCluster_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (Context == null) return;
            var existingNames = Context.Clusters.Select(c => c.Name).ToList();
            var validator = new Func<string, System.Threading.Tasks.Task<bool>>(Context.ValidateConnectionAsync);
            var dialog = new AddEditClusterDialog(existingNames, validator);
            var result = await dialog.ShowDialog<ClusterInfo?>(this);

            if (result != null)
            {
                await Context.AddClusterAsync(result.Name, result.Address);
            }
        }
        catch (Exception ex)
        {
            await ShowError(ex.Message);
        }
    }

    private async void EditCluster_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var selected = ClustersGrid.SelectedItem as ClusterViewModel;
            if (selected == null) return;

            if (Context == null) return;
            var existingNames = Context.Clusters.Select(c => c.Name).ToList();
            var validator = new Func<string, System.Threading.Tasks.Task<bool>>(Context.ValidateConnectionAsync);
            var clusterInfo = new ClusterInfo(selected.Id, selected.Name, selected.Address);
            var dialog = new AddEditClusterDialog(clusterInfo, existingNames, validator);
            var result = await dialog.ShowDialog<ClusterInfo?>(this);

            if (result != null)
            {
                await Context.UpdateClusterAsync(selected, result.Name, result.Address);
            }
        }
        catch (Exception ex)
        {
            await ShowError(ex.Message);
        }
    }

    private async void RemoveCluster_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var selected = ClustersGrid.SelectedItem as ClusterViewModel;
            Context?.RemoveCluster(selected);
        }
        catch (Exception ex)
        {
            await ShowError(ex.Message);
        }
    }
    #endregion

    #region KafkaLens Clients
    private async void AddClient_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (Context == null) return;
            var existingNames = Context.Clients.Select(c => c.Name).ToList();
            var dialog = new AddEditClientDialog(existingNames);
            var result = await dialog.ShowDialog<ClientInfo?>(this);

            if (result != null)
            {
                Context.AddClient(result.Name, result.Address, result.Protocol);
            }
        }
        catch (Exception ex)
        {
            await ShowError(ex.Message);
        }
    }

    private async void EditClient_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var selected = ClientsGrid.SelectedItem as ClientInfoViewModel;
            if (selected == null) return;

            if (Context == null) return;
            var existingNames = Context.Clients.Select(c => c.Name).ToList();
            var dialog = new AddEditClientDialog(selected.Info, existingNames);
            var result = await dialog.ShowDialog<ClientInfo?>(this);

            if (result != null)
            {
                Context.UpdateClient(result);
            }
        }
        catch (Exception ex)
        {
            await ShowError(ex.Message);
        }
    }

    private async void RemoveClient_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var selected = ClientsGrid.SelectedItem as ClientInfoViewModel;
            Context?.RemoveClient(selected);
        }
        catch (Exception ex)
        {
            await ShowError(ex.Message);
        }
    }
    #endregion

    private async System.Threading.Tasks.Task ShowError(string message)
    {
         var box = MessageBoxManager
            .GetMessageBoxStandard("Error", message, ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Error);
         await box.ShowAsync();
    }

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