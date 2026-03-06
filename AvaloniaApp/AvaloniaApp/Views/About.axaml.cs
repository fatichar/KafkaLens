using System;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;
using AvaloniaApp.Utils;
using KafkaLens.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace AvaloniaApp.Views;

public partial class About : Window
{
    private const string AUTHOR = "Pravin Chaudhary";
    public string AppVersion { get; }
    public string Copyright { get; } = $"Copyright {DateTime.Now.Year}: " + AUTHOR;

    public About()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        AppVersion = version != null ? $"Version: {version.ToString(2)}" : "Version: unknown";
        DataContext = this;
        InitializeComponent();
#if DEBUG
        this.AttachDevTools();
#endif
    }

    private void CheckForUpdates_OnClick(object? sender, RoutedEventArgs e)
    {
        var mainViewModel = App.Current.Services.GetRequiredService<MainViewModel>();
        _ = mainViewModel.CheckForUpdatesAsync(false);
    }

    private void Url_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { CommandParameter: string url })
        {
            OsUtils.OpenExternal(url);
        }
    }
}