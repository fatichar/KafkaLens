using KafkaLens.App.ViewModels;
using System.Windows.Controls;

namespace KafkaLens.App.Controls
{
    public partial class MessageDisplayOptionsPanel : UserControl
    {
        public MessageDisplayOptionsPanel()
        {
            InitializeComponent();
        }

        private OpenedClusterViewModel Context => (OpenedClusterViewModel)DataContext;
    }
}
