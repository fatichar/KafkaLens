using System.ComponentModel;
using System.Linq;
using Avalonia.Input;
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
    // TODO: Implement message table filtering
    // private string messageTablePositiveFilter = "";
    // private string messageTableNegativeFilter = "";
    private MessageViewModel? subscribedMessage;
    private OpenedClusterViewModel? previousContext;
    private TextMate.Installation? _textMateInstallation;
    private RegistryOptions? _registryOptions;

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
            ApplyTextMateTheme(m.Value);
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
        var currentTheme = GetCurrentTextMateTheme();
        _registryOptions = new RegistryOptions(currentTheme);
        _textMateInstallation = MessageViewer.InstallTextMate(_registryOptions);

        var jsonLanguage = _registryOptions.GetLanguageByExtension(".json");
        if (jsonLanguage != null)
        {
            _textMateInstallation.SetGrammar(_registryOptions.GetScopeByLanguageId(jsonLanguage.Id));
        }
    }

    private void ApplyTextMateTheme(string themeName)
    {
        var textMateTheme = themeName switch
        {
            "Dark" => ThemeName.DarkPlus,
            "Light" => ThemeName.LightPlus,
            _ => GetSystemTextMateTheme()
        };

        _registryOptions = new RegistryOptions(textMateTheme);
        _textMateInstallation?.Dispose();
        _textMateInstallation = MessageViewer.InstallTextMate(_registryOptions);

        var jsonLanguage = _registryOptions.GetLanguageByExtension(".json");
        if (jsonLanguage != null)
        {
            _textMateInstallation.SetGrammar(_registryOptions.GetScopeByLanguageId(jsonLanguage.Id));
        }
    }

    private ThemeName GetCurrentTextMateTheme()
    {
        var app = Avalonia.Application.Current;
        if (app?.ActualThemeVariant == ThemeVariant.Dark)
        {
            return ThemeName.DarkPlus;
        }
        return ThemeName.LightPlus;
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

    private void messagesGrid_LoadingRow(object sender, DataGridRowEventArgs e)
    {
        e.Row.Header = 1 + e.Row.Index;
    }

    private void UserControl_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers == KeyModifiers.Control && e.Key == Key.F)
        {
            var positiveFilterBox = MessagesToolbar.FindControl<TextBox>("PositiveFilterBox");
            positiveFilterBox?.Focus();
            e.Handled = true;
        }
    }
}