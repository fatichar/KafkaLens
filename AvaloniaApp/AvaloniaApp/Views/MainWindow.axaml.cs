using System;
using Avalonia.Controls;
using KafkaLens.ViewModels;

namespace AvaloniaApp.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs args)
    {
        var dataContext = DataContext as MainViewModel;
        Title = dataContext?.Title ?? "KafkaLens";
    }
}