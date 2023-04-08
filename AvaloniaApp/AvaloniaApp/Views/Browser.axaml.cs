using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using AvaloniaEdit.Document;
using AvaloniaEdit.Highlighting;
using KafkaLens.ViewModels;

namespace AvaloniaApp.Views;

public partial class Browser : UserControl
{
    private OpenedClusterViewModel dataContext => (OpenedClusterViewModel)DataContext;
    private string messageTablePositiveFilter = "";
    private string messageTableNegativeFilter = "";
    private MessageViewModel? lastMessage = null;
    
    public Browser()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;

        MessageDisplayToolbar.FilterBox.TextChanged += (s, e) =>
        {
            MessageDisplayToolbar.FilterBox.Text.Trim();
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

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
    }

    private void UpdateMessagesView()
    {
        if (dataContext != null)
        {
            dataContext.CurrentMessages.PositiveFilter = messageTablePositiveFilter;
            dataContext.CurrentMessages.NegativeFilter = messageTableNegativeFilter;
        }
    }

    private void MessagesGrid_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var grid = (DataGrid)sender;
        var message = (MessageViewModel?)grid.SelectedItem;
        dataContext.CurrentMessages.CurrentMessage = message;
        if (message != null)
        {
            UpdateMessageText(message);
            lastMessage = message;
        }
        else
        {
            lastMessage = null;
            SetText("");
        }
    }

    private void UpdateMessageText(MessageViewModel message)
    {
        //// this will update
        lastMessage?.Cleanup();
        message.PrettyFormat();
        SetText(message.DisplayText);

        UpdateHighlighting();
    }

    private void SetText(string message)
    {
        message ??= "";
        MessageViewer.Document = new TextDocument(message.ToCharArray());
    }

    private void UpdateHighlighting()
    {
        var messageSource = (IMessageSource?)dataContext?.SelectedNode;
        MessageViewer.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("Json");
    }

    private void messagesGrid_LoadingRow(object sender, DataGridRowEventArgs e)
    {
        e.Row.Header = e.Row.GetIndex();
    }
}