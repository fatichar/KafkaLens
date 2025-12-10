using System;
using System.Linq;
using Avalonia.Controls;
using AvaloniaEdit.Document;
using AvaloniaEdit.Highlighting;
using KafkaLens.ViewModels;

namespace AvaloniaApp.Views;

public partial class Browser : UserControl
{
    private OpenedClusterViewModel Context => (OpenedClusterViewModel)DataContext!;
    private string messageTablePositiveFilter = "";
    private string messageTableNegativeFilter = "";

    public Browser()
    {
        InitializeComponent();

        UpdateHighlighting();

        MessageDisplayToolbar.FilterBox.TextChanged += (s, e) =>
        {
            var message = Context.CurrentMessages.CurrentMessage;
            if (message != null)
            {
                SetText(message.DisplayText);
            }
        };

        MessageDisplayToolbar.FormatterCombo.SelectionChanged += (s, e) =>
        {
            var message = Context?.CurrentMessages?.CurrentMessage;
            if (message != null && MessageDisplayToolbar.FormatterCombo.SelectedItem != null)
            {
                message.FormatterName = MessageDisplayToolbar.FormatterCombo.SelectedItem.ToString();
                SetText(message.DisplayText);
            }
        };

        MessageDisplayToolbar.ObjectFilterCheckBox.IsCheckedChanged += (s, e) =>
        {
            var message = Context?.CurrentMessages?.CurrentMessage;
            if (message != null)
            {
                message.UseObjectFilter = MessageDisplayToolbar.ObjectFilterCheckBox.IsChecked ?? false;
                SetText(message.DisplayText);
            }
        };
    }

    private void MessagesGrid_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var grid = (DataGrid)sender;
        Context.SelectedMessages = grid.SelectedItems.Cast<MessageViewModel>().ToList();
        var message = (MessageViewModel?)grid.SelectedItem;
        if (message != null)
        {
            SetText(message.DisplayText);
        }
        else
        {
            SetText("");
        }
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