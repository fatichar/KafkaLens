using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using AvaloniaEdit.Highlighting;
using KafkaLens.ViewModels;

namespace AvaloniaApp.Views;

public partial class Browser : UserControl
{
    private OpenedClusterViewModel dataContext => (OpenedClusterViewModel)DataContext;
    private string singleMessageFilter = "";
    private string messageTablePositiveFilter = "";
    private string messageTableNegativeFilter = "";
    private MessageViewModel? lastMessage = null;
    
    public Browser()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;

        // MessageDisplayToolbar.fontSizeSlider.ValueChanged += (s, e) =>
        // {
        //     MessageViewer.FontSize = (int)e.NewValue;
        // };
        //     
        // MessagesPanel.MessagesToolbar.positiveFilterBox.TextChanged += (s, e) =>
        // {
        //     messageTablePositiveFilter = MessagesPanel.MessagesToolbar.positiveFilterBox.Text.Trim();
        //     UpdateMessagesView();
        // };
        //
        // MessagesPanel.MessagesToolbar.negativeFilterBox.TextChanged += (s, e) =>
        // {
        //     messageTableNegativeFilter = MessagesPanel.MessagesToolbar.negativeFilterBox.Text.Trim();
        //     UpdateMessagesView();
        // };

        // MessageDisplayToolbar.filterBox.TextChanged += (s, e) =>
        // {
        //     singleMessageFilter = MessageDisplayToolbar.filterBox.Text.Trim();
        //     var message = dataContext?.CurrentMessages?.CurrentMessage;
        //     if (message != null)
        //     {
        //         UpdateMessageText(message);
        //     }
        // };
        //
        // MessageDisplayToolbar.FormatterCombo.SelectionChanged += (s, e) =>
        // {
        //     var message = dataContext?.CurrentMessages?.CurrentMessage;
        //     if (message != null)
        //     {
        //         UpdateMessageText(message);
        //     }
        // };
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

    private string SetText(string message)
    {
        return MessageViewer.Text = message ?? "";
    }

    private void UpdateHighlighting()
    {
        // var messageSource = (IMessageSource?)dataContext?.SelectedNode;
        // MessageViewer.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("Json");
    }

    private void messagesGrid_LoadingRow(object sender, DataGridRowEventArgs e)
    {
        e.Row.Header = e.Row.GetIndex();
    }
}