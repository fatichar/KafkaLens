using ICSharpCode.AvalonEdit.Highlighting;
using KafkaLens.App.ViewModels;
using System;
using System.ComponentModel;
using System.Windows.Controls;
using System.Windows.Data;

namespace KafkaLens.App.Controls
{
    /// <summary>
    /// Interaction logic for UserControl1.xaml
    /// </summary>
    public partial class Browser : UserControl
    {
        private OpenedClusterViewModel dataContext => (OpenedClusterViewModel)DataContext;
        private string singleMessageFilter = "";
        private string messageTablePositiveFilter = "";
        private string messageTableNegativeFilter = "";

        public Browser()
        {
            InitializeComponent();
            ICollectionView cvTasks = CollectionViewSource.GetDefaultView(messagesGrid.ItemsSource);

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
                var message = dataContext.CurrentMessages.CurrentMessage;
                if (message != null)
                {
                    UpdateMessageText(message);
                }
            };
        }

        private void UpdateMessagesView()
        {
            dataContext.CurrentMessages.PositiveFilter = messageTablePositiveFilter;
            dataContext.CurrentMessages.NegativeFilter = messageTableNegativeFilter;
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
            message.ApplyFilter(singleMessageFilter);
            messageViewer.Document.Text = message.DisplayText;
            
            UpdateHighlighting();
        }

        private void UpdateHighlighting()
        {
            if (string.IsNullOrEmpty(singleMessageFilter))
            {
                messageViewer.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition(dataContext?.SelectedNode?.Formatter?.Name ?? "Json");
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
