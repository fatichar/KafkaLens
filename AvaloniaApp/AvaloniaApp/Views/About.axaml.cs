using Avalonia;
using Avalonia.Controls;
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
}