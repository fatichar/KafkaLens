using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AvaloniaApp.Views;

public partial class MessageDisplayToolbar : UserControl
{
    public MessageDisplayToolbar()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}