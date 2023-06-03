using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using KafkaLens.Shared.Entities;
using KafkaLens.ViewModels;

namespace AvaloniaApp.Views;

public partial class EditClustersDialog : Window
{
    public EditClustersDialog()
    {
        InitializeComponent();
#if DEBUG
        this.AttachDevTools();
#endif
    }

    private void AddButton_Click(object? sender, RoutedEventArgs e)
    {
        var clusterInfo = new ClusterInfo(CreateNewId(NameBox.Text), NameBox.Text, AddressBox.Text, "");
        Context.Add(clusterInfo);
        
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
    }

    private void RemoveButton_Click(object? sender, RoutedEventArgs e)
    {
        if (ClustersGrid.SelectedItem is ClusterInfo clusterInfo)
        {
            Context.Remove(clusterInfo);
        }
    }

    private void OnClose(object? sender, WindowClosingEventArgs e)
    {
        Save();
    }
}