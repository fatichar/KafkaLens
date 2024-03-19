using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ActiproSoftware.UI.Avalonia.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace AvaloniaApp.Views;

public partial class About : Window
{
    public About()
    {
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
    
    public void OpenUrl(object urlObj)
    {
        var url = urlObj as string;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            //https://stackoverflow.com/a/2796367/241446
            using var proc = new Process { StartInfo = { UseShellExecute = true, FileName = url } };
            proc.Start();

            return;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Process.Start("x-www-browser", url);
            return;
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) throw new ArgumentException("invalid url: " + url);
        Process.Start("open", url);
        return;
    }
}