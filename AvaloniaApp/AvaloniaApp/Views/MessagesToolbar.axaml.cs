using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AvaloniaApp.Views;

public partial class MessagesToolbar : UserControl
{
    public MessagesToolbar()
    {
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}