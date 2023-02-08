using CommunityToolkit.WinUI.UI.Controls;
using KafkaLens.ViewModels;
using Microsoft.UI.Xaml.Documents;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using CommunityToolkit.WinUI.UI.Controls.TextToolbarSymbols;
using Uno.Extensions.Specialized;

namespace KafkaLens.Views
{
    /// <summary>
    /// Interaction logic for UserControl1.xaml
    /// </summary>
    public partial class MessageBrowser : UserControl
    {
        private OpenedClusterViewModel dataContext => (OpenedClusterViewModel)DataContext;

        public MessageBrowser()
        {
            InitializeComponent();

            messagesGrid.LoadingRow += OnLoadingRow;
        }

        public void SaveMenuItem_Click(object sender, RoutedEventArgs e)
        {
            SaveMessages(messagesGrid.SelectedItems);
        }

        public void SaveAllMenuItem_Click(object sender, RoutedEventArgs e)
        {
            SaveMessages(messagesGrid.ItemsSource);
        }

        private void SaveMessages(IEnumerable items)
        {
            var messages = new List<MessageViewModel>();
            foreach (var item in items)
            {
                messages.Add((MessageViewModel)item);
            }

            Save(messages);
        }

        private void Save(List<MessageViewModel> messages)
        {
            messages.ForEach(Save);
        }

        private void Save(MessageViewModel message)
        {
            
        }

        void OnMessagesGridSort(object sender, DataGridColumnEventArgs e)
        {
            //Use the Tag property to pass the bound column name for the sorting implementation
            //if (e.Column.Header.ToString() == "Offset")
            //{
            //    //Implement sort on the column "Range" using LINQ
            //    if (e.Column.SortDirection == null || e.Column.SortDirection == DataGridSortDirection.Descending)
            //    {
            //        messagesGrid.ItemsSource = new ObservableCollection<MessageViewModel>(from item in dataContext.CurrentMessages.Filtered
            //                                                                              orderby item.Offset ascending
            //                                                            select item);
            //        e.Column.SortDirection = DataGridSortDirection.Ascending;
            //    }
            //    else
            //    {
            //        messagesGrid.ItemsSource = new ObservableCollection<MessageViewModel>(from item in dataContext.CurrentMessages.Filtered
            //                                                                              orderby item.Offset descending
            //                                                            select item);
            //        e.Column.SortDirection = DataGridSortDirection.Descending;
            //    }
            //}
            //// add code to handle sorting by other columns as required

            //// Remove sorting indicators from other columns
            //foreach (var column in messagesGrid.Columns)
            //{
            //    if (column.Header.ToString() != e.Column.Header.ToString())
            //    {
            //        column.SortDirection = null;
            //    }
            //}
        }

            //private void OnDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs e)
            //{
            //    if (e.NewValue != null)
            //    {
            //        messagesGrid.SelectionChanged += messagesGrid_OnSelectionChanged;
            //    }
            //    else
            //    {
            //        messagesGrid.SelectionChanged -= messagesGrid_OnSelectionChanged;
            //    }
            //}

            //private void UpdateMessagesView()
            //{
            //    dataContext.CurrentMessages.PositiveFilter = messageTablePositiveFilter;
            //    dataContext.CurrentMessages.NegativeFilter = messageTableNegativeFilter;
            //}

            //private void messagesGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
            //{
            //    var message = (MessageViewModel?)(messagesGrid.SelectedItem);
            //    dataContext.CurrentMessages.CurrentMessage = message;
            //    if (message != null)
            //    {
            //        UpdateMessageText(message);
            //    }
            //    else
            //    {
            //        messageViewer.Text = "";
            //    }
            //}

            //private void UpdateMessageText(MessageViewModel message)
            //{
            //    // this will update DisplayText
            //    message.ApplyFilter(singleMessageFilter);
            //    messageViewer.Text = message.DisplayText;

            //    UpdateHighlighting();
            //}

            private void UpdateHighlighting()
        {
            //if (string.IsNullOrEmpty(singleMessageFilter))
            //{
            //    var messageSource = (IMessageSource?)dataContext?.SelectedNode;
            //    messageViewer.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition(messageSource?.Formatter?.Name ?? "Json");
            //}
            //else
            //{
            //    messageViewer.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("Text");
            //}
        }

        private void OnLoadingRow(object? sender, DataGridRowEventArgs e)
        {
            e.Row.Header = e.Row.GetIndex();
        }
    }
}
