using System.Windows;
using System.Windows.Controls;
using ICSharpCode.AvalonEdit.Highlighting;
using KafkaLens.ViewModels;

namespace KafkaLens.App.Controls;

public partial class MessagesPanel
{
    public MessagesPanel()
    {
        InitializeComponent();
    }

    private void messagesGrid_LoadingRow(object sender, DataGridRowEventArgs e)
    {
        e.Row.Header = e.Row.GetIndex();
    }
}