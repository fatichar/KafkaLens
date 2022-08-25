using KafkaLens.App.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace KafkaLens.App.Controls
{
    /// <summary>
    /// Interaction logic for UserControl1.xaml
    /// </summary>
    public partial class Navigator : UserControl
    {
        public Navigator()
        {
            InitializeComponent();
        }

        private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (dataContext != null)
            {
                if (dataContext.SelectedNode != (IMessageSource?)e.NewValue)
                {
                    dataContext.SelectedNode = (IMessageSource?)e.NewValue;
                }
            }
            e.Handled = true;
        }

        private OpenedClusterViewModel dataContext => (OpenedClusterViewModel)DataContext; 
    }
}
