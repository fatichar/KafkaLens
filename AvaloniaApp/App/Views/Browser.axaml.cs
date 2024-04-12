﻿using System;
using System.Linq;
using Avalonia.Controls;
using AvaloniaEdit.Document;
using AvaloniaEdit.Highlighting;
using KafkaLens.ViewModels;

namespace App.Views;

public partial class Browser : UserControl
{
    private OpenedClusterViewModel dataContext => (OpenedClusterViewModel)DataContext;
    private string messageTablePositiveFilter = "";
    private string messageTableNegativeFilter = "";

    public Browser()
    {
        InitializeComponent();

        UpdateHighlighting();

        MessageDisplayToolbar.FilterBox.TextChanged += (s, e) =>
        {
            var message = dataContext?.CurrentMessages?.CurrentMessage;
            if (message != null)
            {
                SetText(message.DisplayText);
            }
        };

        MessageDisplayToolbar.FormatterCombo.SelectionChanged += (s, e) =>
        {
            var message = dataContext?.CurrentMessages?.CurrentMessage;
            if (message != null && MessageDisplayToolbar.FormatterCombo.SelectedItem != null)
            {
                message.FormatterName = MessageDisplayToolbar.FormatterCombo.SelectedItem.ToString();
                SetText(message.DisplayText);
            }
        };
    }

    private void MessagesGrid_OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var grid = (DataGrid)sender!;
        dataContext.SelectedMessages = grid.SelectedItems.Cast<MessageViewModel>().ToList();
        var message = (MessageViewModel?)grid.SelectedItem;
        if (message != null)
        {
            SetText(message.DisplayText);
        }
        else
        {
            SetText("");
        }
        MessagesGrid.ScrollIntoView(grid.SelectedItem, null);
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