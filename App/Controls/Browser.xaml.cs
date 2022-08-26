using ICSharpCode.AvalonEdit.Highlighting;
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
            messageDisplayOptionsPanel.fontSizeSlider.ValueChanged += (s, e) =>
            {
                messageViewer.FontSize = (int)e.NewValue;
            };
        }

        private void Selector_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var message = (MessageViewModel?)(messagesGrid.SelectedItem ?? messagesGrid.CurrentItem);
            dataContext.CurrentMessages.CurrentMessage = message;
            if (message != null)
            {
                messageViewer.Document.Text = message.Message;
                messageViewer.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition(dataContext?.SelectedNode?.Formatter?.Name ?? "Json");
                messageViewer.FontSize = dataContext?.FontSize ?? 14;
            }
            else
            {
                messageViewer.Document.Text = "";
            }
        }
    }
}
