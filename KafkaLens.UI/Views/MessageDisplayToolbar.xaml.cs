using KafkaLens.ViewModels;

namespace KafkaLens.Views
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
