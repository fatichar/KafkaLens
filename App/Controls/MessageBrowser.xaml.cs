using ICSharpCode.AvalonEdit.Highlighting;
using KafkaLens.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace KafkaLens.App.Controls
{
    public partial class MessageBrowser : UserControl
    {
        private OpenedClusterViewModel dataContext => (OpenedClusterViewModel)DataContext;
        private string singleMessageFilter = "";
        private string messageTablePositiveFilter = "";
        private string messageTableNegativeFilter = "";

        public MessageBrowser()
        {
            InitializeComponent();

            DataContextChanged += OnDataContextChanged;

            messageDisplayToolbar.fontSizeSlider.ValueChanged += (s, e) =>
            {
                messageViewer.FontSize = (int)e.NewValue;
            };
            
            messagesToolbar.positiveFilterBox.TextChanged += (s, e) =>
            {
                messageTablePositiveFilter = messagesToolbar.positiveFilterBox.Text.Trim();
                UpdateMessagesView();
            };

            messagesToolbar.negativeFilterBox.TextChanged += (s, e) =>
            {
                messageTableNegativeFilter = messagesToolbar.negativeFilterBox.Text.Trim();
                UpdateMessagesView();
            };

            messageDisplayToolbar.filterBox.TextChanged += (s, e) =>
            {
                singleMessageFilter = messageDisplayToolbar.filterBox.Text.Trim();
                var message = dataContext?.CurrentMessages?.CurrentMessage;
                if (message != null)
                {
                    UpdateMessageText(message);
                }
            };
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.OldValue == null)
            {
                messagesGrid.SelectionChanged += messagesGrid_OnSelectionChanged;
            } 
            else if (e.NewValue == null)
            {
                messagesGrid.SelectionChanged -= messagesGrid_OnSelectionChanged;
            }
        }

        private void UpdateMessagesView()
        {
            if (dataContext != null)
            {
                dataContext.CurrentMessages.PositiveFilter = messageTablePositiveFilter;
                dataContext.CurrentMessages.NegativeFilter = messageTableNegativeFilter;
            }
        }

        private void messagesGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var message = (MessageViewModel?)messagesGrid.SelectedItem;
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
            //// this will update DisplayText
            messageViewer.Document.Text = message.DisplayText;

            UpdateHighlighting();
        }

        private void UpdateHighlighting()
        {
            if (string.IsNullOrEmpty(singleMessageFilter))
            {
                var messageSource = (IMessageSource?)dataContext?.SelectedNode;
                messageViewer.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition(messageSource?.Formatter?.Name ?? "Json");
            }
            else
            {
                messageViewer.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("Text");
            }
        }

        private void messagesGrid_LoadingRow(object sender, DataGridRowEventArgs e)
        {
            e.Row.Header = e.Row.GetIndex();
        }
    }
}
