using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AvaloniaApp.Views;

public partial class Browser : UserControl
{
    public Browser()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}