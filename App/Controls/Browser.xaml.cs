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
        private string filterText = "";

        public Browser()
        {
            InitializeComponent();
            
            messageDisplayOptionsPanel.fontSizeSlider.ValueChanged += (s, e) =>
            {
                messageViewer.FontSize = (int)e.NewValue;
            };
            
            messageDisplayOptionsPanel.filterBox.TextChanged += (s, e) =>
            {
                filterText = messageDisplayOptionsPanel.filterBox.Text.Trim();
                var message = dataContext.CurrentMessages.CurrentMessage;
                if (message != null)
                {
                    UpdateMessageText(message);
                }
            };
        }

        private void Selector_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var message = (MessageViewModel?)(messagesGrid.SelectedItem ?? messagesGrid.CurrentItem);
            dataContext.CurrentMessages.CurrentMessage = message;
            if (message != null)
            {
                UpdateMessageText(message);
            }
            else
            {
                messageViewer.Document.Text = "";
            }
        }

        private void UpdateMessageText(MessageViewModel message)
        {
            // this will update DisplayText
            message.ApplyFilter(filterText);
            messageViewer.Document.Text = message.DisplayText;
            
            UpdateHighlighting();
        }

        private void UpdateHighlighting()
        {
            if (string.IsNullOrEmpty(filterText))
            {
                messageViewer.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition(dataContext?.SelectedNode?.Formatter?.Name ?? "Json");
            }
            else
            {
                messageViewer.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("Text");
            }
        }
    }
}
