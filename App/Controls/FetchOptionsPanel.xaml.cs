using KafkaLens.App.ViewModels;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using System.Windows.Input;

namespace KafkaLens.App.Controls
{
    public partial class FetchOptionsPanel : UserControl
    {
        private OpenedClusterViewModel dataContext => (OpenedClusterViewModel)DataContext;
        
        public FetchOptionsPanel()
        {
            InitializeComponent();
        }

        private void AcceptOffset(object sender, TextCompositionEventArgs e)
        {
            e.Handled = new Regex("[^0-9]+").IsMatch(e.Text);
        }
    }
}
