using System;
using Avalonia.Controls;
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

        UpdateHighlighting();

        DataContextChanged += OnDataContextChanged;

        MessageDisplayToolbar.FilterBox.TextChanged += (s, e) =>
        {
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
                message.FormatterName = MessageDisplayToolbar.FormatterCombo.SelectedItem.ToString();
                UpdateMessageText(message);
            }
        };
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        // UpdateMessagesView();
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
        // dataContext.CurrentMessages.CurrentMessage = message;
        if (message != null)
        {
            lastMessage?.Cleanup();
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
        SetText(message.DisplayText);
    }

    private void SetText(string message)
    {
        message ??= "";
        if (MessageViewer != null)
        {
            if (MessageViewer.Document == null)
            {
                MessageViewer.Document = new TextDocument();
            }
            MessageViewer.Document.Text = message;
        }
    }

    private void UpdateHighlighting()
    {
        MessageViewer.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("Json");
    }

    // private void messagesGrid_LoadingRow(object sender, DataGridRowEventArgs e)
    // {
    //     e.Row.Header = e.Row.GetIndex();
    // }
    //
    // private void MessageViewer_OnDocumentChanged(object? sender, EventArgs e)
    // {
    //     SetText(dataContext?.CurrentMessages?.CurrentMessage?.DisplayText);
    // }
}