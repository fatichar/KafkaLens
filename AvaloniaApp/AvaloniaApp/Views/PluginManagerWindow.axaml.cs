using Avalonia.Markup.Xaml;

namespace AvaloniaApp.Views;

public partial class PluginManagerWindow : Avalonia.Controls.Window
{
    public PluginManagerWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
