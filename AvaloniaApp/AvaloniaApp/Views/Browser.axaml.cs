using System.ComponentModel;
using System.Linq;
using System;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Controls;
using Avalonia.Styling;
using AvaloniaEdit.Document;
using AvaloniaEdit.TextMate;
using CommunityToolkit.Mvvm.Messaging;
using KafkaLens.ViewModels;
using KafkaLens.ViewModels.Messages;
using TextMateSharp.Grammars;

namespace AvaloniaApp.Views;

public partial class Browser : UserControl
{
    private OpenedClusterViewModel? Context => (OpenedClusterViewModel?)DataContext;
    private MessageViewModel? subscribedMessage;
    private OpenedClusterViewModel? previousContext;
    private TextMate.Installation? textMateInstallation;
    private RegistryOptions? registryOptions;

    public Browser()
    {
        InitializeComponent();

        SetupTextMateHighlighting();

        // Use tunnel routing so Ctrl+F is intercepted before TextEditor's built-in find
        AddHandler(KeyDownEvent, UserControl_KeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        // Subscribe to CurrentMessage changes to update TextEditor
        this.PropertyChanged += (s, e) =>
        {
            if (e.Property.Name == nameof(DataContext))
            {
                OnDataContextChanged();
            }
        };

        WeakReferenceMessenger.Default.Register<ThemeChangedMessage>(this, (r, m) =>
        {
            OnThemeChanged(m.Value);
        });
    }

    private void OnDataContextChanged()
    {
        // Unsubscribe from previous context
        if (previousContext != null)
        {
            previousContext.CurrentMessages.PropertyChanged -= OnCurrentMessagesChanged;
        }
        if (subscribedMessage != null)
        {
            subscribedMessage.PropertyChanged -= OnCurrentMessagePropertyChanged;
            subscribedMessage = null;
        }

        previousContext = Context;

        if (Context != null)
        {
            Context.CurrentMessages.PropertyChanged += OnCurrentMessagesChanged;
            Context.SetClipboardText = async text =>
            {
                var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
                if (clipboard != null)
                    await clipboard.SetTextAsync(text);
            };
            ApplySavedSort(Context);

            // Restore display if there's already a selected message
            var message = Context.CurrentMessages.CurrentMessage;
            if (message != null)
            {
                subscribedMessage = message;
                message.PropertyChanged += OnCurrentMessagePropertyChanged;
                SetText(message.DisplayText);
            }
            else
            {
                SetText("");
            }
        }
        else
        {
            SetText("");
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

    private void MessagesGrid_OnSorting(object? sender, DataGridColumnEventArgs e)
    {
        if (Context == null || e.Column == null)
        {
            return;
        }

        var clickedColumn = e.Column.Header?.ToString();
        var sameColumn = string.Equals(Context.MessagesSortColumn, clickedColumn, StringComparison.Ordinal);
        var nextAscending = !(sameColumn && Context.MessagesSortAscending == true);

        Context.MessagesSortColumn = clickedColumn;
        Context.MessagesSortAscending = nextAscending;
    }

    private void ApplySavedSort(OpenedClusterViewModel context)
    {
        if (string.IsNullOrWhiteSpace(context.MessagesSortColumn) || context.MessagesSortAscending == null)
        {
            return;
        }

        var column = MessagesGrid.Columns.FirstOrDefault(c =>
            string.Equals(c.Header?.ToString(), context.MessagesSortColumn, StringComparison.Ordinal));

        if (column == null)
        {
            return;
        }

        column.Sort(context.MessagesSortAscending.Value
            ? ListSortDirection.Ascending
            : ListSortDirection.Descending);
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

    private void SetupTextMateHighlighting()
    {
        ApplyTextMateTheme(GetSystemTextMateTheme());
    }

    private void OnThemeChanged(string themeName)
    {
        var textMateTheme = themeName switch
        {
            "Dark" => ThemeName.DarkPlus,
            "Gray" => ThemeName.Dark,
            "Light" => ThemeName.LightPlus,
            _ => GetSystemTextMateTheme()
        };

        ApplyTextMateTheme(textMateTheme);
    }

    private void ApplyTextMateTheme(ThemeName textMateTheme)
    {
        registryOptions = new RegistryOptions(textMateTheme);
        textMateInstallation?.Dispose();
        textMateInstallation = MessageViewer.InstallTextMate(registryOptions);

        var jsonLanguage = registryOptions.GetLanguageByExtension(".json");
        if (jsonLanguage != null)
        {
            textMateInstallation.SetGrammar(registryOptions.GetScopeByLanguageId(jsonLanguage.Id));
        }

        ApplySelectionColors();
    }

    private static ThemeName GetSystemTextMateTheme()
    {
        var app = Avalonia.Application.Current;
        if (app?.ActualThemeVariant == ThemeVariant.Dark)
        {
            return ThemeName.DarkPlus;
        }
        return ThemeName.LightPlus;
    }

    private void ApplySelectionColors()
    {
        if (MessageViewer?.TextArea == null) return;

        var app = Avalonia.Application.Current;
        if (app?.Resources == null) return;

        if (app.Resources.TryGetResource("ThemeSelectionBrush", null, out var selectionBrush) && selectionBrush is Avalonia.Media.IBrush brush)
        {
            MessageViewer.TextArea.SelectionBrush = brush;
        }

        if (app.Resources.TryGetResource("ThemeSelectionForegroundBrush", null, out var selectionForeground) && selectionForeground is Avalonia.Media.IBrush fgBrush)
        {
            MessageViewer.TextArea.SelectionForeground = fgBrush;
        }
    }

    private void messagesGrid_LoadingRow(object sender, DataGridRowEventArgs e)
    {
        e.Row.Header = 1 + e.Row.Index;
    }

    private void UserControl_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e is not { KeyModifiers: KeyModifiers.Control, Key: Key.F }) return;

        var filterBox = MessagesToolbar.FindControl<TextBox>("FilterBox");
        filterBox?.Focus();
        e.Handled = true;
    }
}
