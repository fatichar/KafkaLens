using ICSharpCode.AvalonEdit.Highlighting;
using KafkaLens.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace KafkaLens.App.Controls;

public partial class TopicPartitionBrowser : UserControl
{
    private OpenedClusterViewModel dataContext => (OpenedClusterViewModel)DataContext;
    private string singleMessageFilter = "";
    private string messageTablePositiveFilter = "";
    private string messageTableNegativeFilter = "";
    private MessageViewModel? lastMessage = null;

    public TopicPartitionBrowser()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;

        MessageDisplayToolbar.fontSizeSlider.ValueChanged += (s, e) =>
        {
            MessageViewer.FontSize = (int)e.NewValue;
        };
            
        MessagesPanel.MessagesToolbar.positiveFilterBox.TextChanged += (s, e) =>
        {
            messageTablePositiveFilter = MessagesPanel.MessagesToolbar.positiveFilterBox.Text.Trim();
            UpdateMessagesView();
        };

        MessagesPanel.MessagesToolbar.negativeFilterBox.TextChanged += (s, e) =>
        {
            messageTableNegativeFilter = MessagesPanel.MessagesToolbar.negativeFilterBox.Text.Trim();
            UpdateMessagesView();
        };

        MessageDisplayToolbar.filterBox.TextChanged += (s, e) =>
        {
            singleMessageFilter = MessageDisplayToolbar.filterBox.Text.Trim();
            var message = dataContext?.CurrentMessages?.CurrentMessage;
            if (message != null)
            {
                UpdateMessageText(message);
            }
        };
        
        MessageDisplayToolbar.FormatterCombo.SelectionChanged += (s, e) =>
        {
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
            MessagesPanel.MessagesGrid.SelectionChanged += messagesGrid_OnSelectionChanged;
        } 
        else if (e.NewValue == null)
        {
            MessagesPanel.MessagesGrid.SelectionChanged -= messagesGrid_OnSelectionChanged;
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
        var message = (MessageViewModel?)MessagesPanel.MessagesGrid.SelectedItem;
        dataContext.CurrentMessages.CurrentMessage = message;
        if (message != null)
        {
            UpdateMessageText(message);
            lastMessage = message;
        }
        else
        {
            lastMessage = null;
            MessageViewer.Document.Text = "";
        }
    }

    private void UpdateMessageText(MessageViewModel message)
    {
        //// this will update
        lastMessage?.Cleanup();
        message.PrettyFormat();
        MessageViewer.Document.Text = message.DisplayText ?? "";

        UpdateHighlighting();
    }

    private void UpdateHighlighting()
    {
        if (true)
        {
            var messageSource = (IMessageSource?)dataContext?.SelectedNode;
            MessageViewer.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("Json");
        }
        else
        {
            MessageViewer.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("Text");
        }
    }

    private void messagesGrid_LoadingRow(object sender, DataGridRowEventArgs e)
    {
        e.Row.Header = e.Row.GetIndex();
    }
}