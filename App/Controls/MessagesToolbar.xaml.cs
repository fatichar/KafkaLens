using KafkaLens.ViewModels;
using System.Windows.Controls;

namespace KafkaLens.App.Controls;

public partial class MessagesToolbar : UserControl
{
    public MessagesToolbar()
    {
        InitializeComponent();
    }

    private OpenedClusterViewModel Context => (OpenedClusterViewModel)DataContext;
}