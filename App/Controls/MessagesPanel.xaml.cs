using System.Windows.Controls;

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