using KafkaLens.App.ViewModels;
using System.Windows.Controls;

namespace KafkaLens.App.Controls
{
    /// <summary>
    /// Interaction logic for UserControl1.xaml
    /// </summary>
    public partial class Browser : UserControl
    {
        private OpenedClusterViewModel dataContext => (OpenedClusterViewModel)DataContext;

        public Browser()
        {
            InitializeComponent();
        }


        private void Selector_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {

            if (dataContext != null && messagesGrid.SelectedItem != null)
            {
                dataContext.CurrentMessages.CurrentMessage = (MessageViewModel)messagesGrid.SelectedItem;
            }
            else
            {
                dataContext.CurrentMessages.CurrentMessage = null;
            }
                
            
        }
    }
}
