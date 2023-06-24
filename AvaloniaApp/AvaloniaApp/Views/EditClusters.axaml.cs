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

    private void AddClusterButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            Context.Add(NameBox.Text, AddressBox.Text);
        
            NameBox.Clear();
            AddressBox.Clear();
        }
        catch (Exception ex)
        {
            ErrorLabel.Content = ex.Message;
        }
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
        AddClusterButton.IsEnabled = !string.IsNullOrWhiteSpace(NameBox.Text) &&
                                      !string.IsNullOrWhiteSpace(AddressBox.Text);
    }
}