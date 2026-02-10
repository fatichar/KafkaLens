using System;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using AvaloniaEdit.Document;
using AvaloniaEdit.Highlighting;
using KafkaLens.ViewModels;

namespace AvaloniaApp.Views;

public partial class Browser : UserControl
{
    private OpenedClusterViewModel? Context => (OpenedClusterViewModel?)DataContext;
    private string messageTablePositiveFilter = "";
    private string messageTableNegativeFilter = "";
    private MessageViewModel? subscribedMessage;

    public Browser()
    {
        InitializeComponent();

        UpdateHighlighting();

        // Subscribe to CurrentMessage changes to update TextEditor
        this.PropertyChanged += (s, e) =>
        {
            if (e.Property.Name == nameof(DataContext))
            {
                OnDataContextChanged();
            }
        };
    }

    private void OnDataContextChanged()
    {
        if (Context != null)
        {
            Context.CurrentMessages.PropertyChanged += OnCurrentMessagesChanged;

            // Restore display if there's already a selected message
            var message = Context.CurrentMessages.CurrentMessage;
            if (message != null)
            {
                subscribedMessage = message;
                message.PropertyChanged += OnCurrentMessagePropertyChanged;
                SetText(message.DisplayText);
            }
        }
    }

    private void OnCurrentMessagesChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Context.CurrentMessages.CurrentMessage))
        {
            // Unsubscribe from previous message
            if (subscribedMessage != null)
            {
                subscribedMessage.PropertyChanged -= OnCurrentMessagePropertyChanged;
            }

            var message = Context?.CurrentMessages?.CurrentMessage;
            if (message != null)
            {
                // Subscribe to DisplayText changes
                subscribedMessage = message;
                message.PropertyChanged += OnCurrentMessagePropertyChanged;
                SetText(message.DisplayText);
            }
            else
            {
                subscribedMessage = null;
            }
        }
    }

    private void OnCurrentMessagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MessageViewModel.DisplayText))
        {
            if (sender is MessageViewModel message && message == subscribedMessage)
            {
                SetText(message.DisplayText);
            }
        }
    }

    private void MessagesGrid_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is null || Context is null) return;

        var grid = (DataGrid)sender;
        Context.SelectedMessages = grid.SelectedItems.Cast<MessageViewModel>().ToList();
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

    private void messagesGrid_LoadingRow(object sender, DataGridRowEventArgs e)
    {
        e.Row.Header = 1 + e.Row.GetIndex();
    }
}