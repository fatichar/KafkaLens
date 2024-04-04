using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using KafkaLens.Shared.Entities;
using KafkaLens.ViewModels;

namespace AvaloniaApp.Views;

public partial class EditClustersDialog : Window
{
    private string fileExplorerCommand = "";
    private ClusterInfo? selectedItem;
    private string AppDataPath { get; set; }

    public EditClustersDialog()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        AppDataPath = Path.Combine(appDataPath, "KafkaLens");

        InitPlatform();

        InitializeComponent();
#if DEBUG
        this.AttachDevTools();
#endif
    }

    private void AddClusterButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (EditMode)
            {
                SelectedItem!.Name = NameBox.Text;
                SelectedItem!.Address = AddressBox.Text;
                Save();
            }
            else
            {
                Context.Add(NameBox.Text, AddressBox.Text);
                ClearInput();
            }
            UpdateUi();
        }
        catch (Exception ex)
        {
            ErrorLabel.Content = ex.Message;
        }
    }

    private void ClearInput()
    {
        NameBox.Clear();
        AddressBox.Clear();
    }

    private static string CreateNewId(string? nameBoxText)
    {
        return Guid.NewGuid().ToString();
    }

    private LocalClustersViewModel Context => DataContext as LocalClustersViewModel;

    private void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        Save();
    }

    private void Save()
    {
        Context.Save();
        var old = Context;
        DataContext = null;
        DataContext = old;
    }

    private void RemoveButton_Click(object? sender, RoutedEventArgs e)
    {
        if (SelectedItem != null)
        {
            Context.Remove(SelectedItem);
        }
    }

    private void OnClose(object? sender, WindowClosingEventArgs e)
    {
        Save();
    }

    private void NameBox_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        UpdateUi();
    }

    private void AddressBox_OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        UpdateUi();
    }

    private void UpdateUi()
    {
        ErrorLabel.Content = string.Empty;

        AddClusterButton.Content = EditMode ? "Update" : "Add";
        var validValues = !string.IsNullOrWhiteSpace(NameBox.Text) &&
                                      !string.IsNullOrWhiteSpace(AddressBox.Text);
        if (EditMode)
        {
            validValues = validValues && (SelectedItem?.Name != NameBox.Text || SelectedItem?.Address != AddressBox.Text);
        }
        AddClusterButton.IsEnabled = validValues;

        RemoveButton.IsEnabled = SelectedItem != null;
    }

    private bool EditMode => SelectedItem != null;

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

    private void ClustersGrid_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var item = ClustersGrid.SelectedItem as ClusterInfo;
        SelectedItem = item == null ? null : Context.Clusters.FirstOrDefault(cluster => cluster.Id == item.Id);

        UpdateUi();
    }

    private ClusterInfo? SelectedItem
    {
        get => selectedItem;
        set
        {
            selectedItem = value;
            NameBox.Text = selectedItem?.Name ?? string.Empty;
            AddressBox.Text = selectedItem?.Address ?? string.Empty;
        }
    }
}