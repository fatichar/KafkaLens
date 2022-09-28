using KafkaLens.ViewModels;

namespace KafkaLens.Views
{
    public partial class MessagesToolbar : UserControl
    {
        public MessagesToolbar()
        {
            InitializeComponent();
        }

        private OpenedClusterViewModel Context => (OpenedClusterViewModel)DataContext;
    }
}
