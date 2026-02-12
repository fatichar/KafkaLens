using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using ActiproSoftware.UI.Avalonia.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

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

    private void Url_OnClick(object? sender, RoutedEventArgs e)
    {
        if (sender is HyperlinkTextBlock { CommandParameter: string url })
        {
            OpenUrl(url);
        }
    }

    private void OpenUrl(object urlObj)
    {
        var url = urlObj as string;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            //https://stackoverflow.com/a/2796367/241446
            using var proc = new Process();
            proc.StartInfo.UseShellExecute = true;
            proc.StartInfo.FileName = url;
            proc.Start();

            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Process.Start("x-www-browser", url);
            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Process.Start("open", url);
            return;
        }

        throw new ArgumentException("invalid url: " + url);
    }
}