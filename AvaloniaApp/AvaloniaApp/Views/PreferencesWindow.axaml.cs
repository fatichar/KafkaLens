using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using KafkaLens.ViewModels;
using System;

namespace AvaloniaApp.Views;

public partial class PreferencesWindow : Window
{
    public PreferencesWindow()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
