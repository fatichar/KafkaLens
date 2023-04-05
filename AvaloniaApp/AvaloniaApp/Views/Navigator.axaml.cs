using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AvaloniaApp.Views;

public partial class Navigator : UserControl
{
    public Navigator()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}