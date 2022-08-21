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
                dataContext.SelectedNode = e.NewValue;
            }
        }

        private OpenedClusterViewModel dataContext => (OpenedClusterViewModel)DataContext; 
    }
}
