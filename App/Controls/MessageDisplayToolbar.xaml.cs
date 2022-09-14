using KafkaLens.ViewModels;
using System.Windows.Controls;

namespace KafkaLens.App.Controls
{
    public partial class MessageDisplayToolbar : UserControl
    {
        public MessageDisplayToolbar()
        {
            InitializeComponent();
        }

        private OpenedClusterViewModel Context => (OpenedClusterViewModel)DataContext;
    }
}
