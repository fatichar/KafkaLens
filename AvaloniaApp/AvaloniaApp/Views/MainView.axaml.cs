using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaApp.Services;
using AvaloniaApp.Utils;
using KafkaLens.ViewModels;
using KafkaLens.ViewModels.Services;

namespace AvaloniaApp.Views;

public partial class MainView : UserControl
{
    private MainViewModel? subscribedViewModel;

    public MainView()
    {
        InitializeComponent();

        MainViewModel.ShowAboutDialog += () =>
        {
            var mainWindow = GetMainWindow();
            if (mainWindow != null)
            {
                var about = new About();
                about.ShowDialog(mainWindow);
            }
        };

        MainViewModel.ShowFolderOpenDialog += OnShowFolderOpenDialog;
        MainViewModel.OpenDiagnosticLogFile += OnOpenDiagnosticLogFile;
        MainViewModel.CopyTextToClipboard += OnCopyTextToClipboard;

        MainViewModel.ShowEditClustersDialog += OnShowEditClustersDialog;

        MainViewModel.ShowUpdateDialog += (updateVm) =>
        {
            var mainWindow = GetMainWindow();
            if (mainWindow != null)
            {
                var dialog = new UpdateDialog { DataContext = updateVm };
                dialog.ShowDialog(mainWindow);
            }
        };

        MainViewModel.ShowMessage += (title, message) =>
        {
            var mainWindow = GetMainWindow();
            if (mainWindow == null)
            {
                return;
            }

            var box = new SimpleMessageBox(title, message, isConfirmation: false);
            _ = box.ShowMessageAsync(mainWindow);
        };

        MainViewModel.ConfirmRestoreTabs = async (count) =>
        {
            var mainWindow = GetMainWindow();
            if (mainWindow == null)
            {
                return false;
            }

            var box = new SimpleMessageBox(
                "Restore Tabs",
                $"You had {count} {(count > 1 ? "tabs" : "tab")} open in the previous session, restore now?",
                isConfirmation: true);

            return await box.ShowConfirmationAsync(mainWindow);
        };
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (subscribedViewModel != null)
        {
            subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            subscribedViewModel.AppLogService.Entries.CollectionChanged -= OnAppLogEntriesChanged;
        }

        base.OnDataContextChanged(e);

        subscribedViewModel = DataContext as MainViewModel;
        if (subscribedViewModel != null)
        {
            subscribedViewModel.PropertyChanged += OnViewModelPropertyChanged;
            subscribedViewModel.AppLogService.Entries.CollectionChanged += OnAppLogEntriesChanged;
        }
    }

    private void OnShowEditClustersDialog()
    {
        var mainWindow = GetMainWindow();
        var dialog = new EditClustersDialog();
        var dataContext = DataContext as MainViewModel;
        if (dataContext == null || mainWindow == null) return;
        dialog.DataContext = new EditClustersViewModel(dataContext.Clusters, dataContext.ClusterInfoRepository, dataContext.ClientInfoRepository, dataContext.ClientFactory);
        dialog.ShowDialog(mainWindow);
    }

    private Window? GetMainWindow()
    {
        return TopLevel.GetTopLevel(this) as Window;
    }

    private async void OnShowFolderOpenDialog()
    {
        var mainWindow = GetMainWindow();
        if (mainWindow == null) return;

        var folders = await mainWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select a folder",
            AllowMultiple = false
        });

        if (folders.Count == 0)
        {
            return;
        }

        var path = folders[0].Path.LocalPath;
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        var dataContext = DataContext as MainViewModel;
        dataContext?.OpenSavedMessages(path);
    }

    private void OnCopyTextToClipboard(string text)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
            _ = clipboard.SetTextAsync(text);
    }

    private void OnOpenDiagnosticLogFile()
    {
        var locator = new SerilogLogFileLocator(App.GetLogPath());
        var logFile = locator.FindLatestLogFile();
        if (string.IsNullOrWhiteSpace(logFile))
        {
            MainViewModel.ShowMessage("Log File", "No diagnostic log file was found yet.");
            return;
        }

        if (!OsUtils.OpenExternal(logFile))
            MainViewModel.ShowMessage("Log File", "Could not open the diagnostic log file.");
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsAppLogPanelVisible) &&
            subscribedViewModel?.IsAppLogPanelVisible == true)
        {
            ScrollAppLogToBottom();
        }
        else if (e.PropertyName is nameof(MainViewModel.AppLogFilterText)
                 or nameof(MainViewModel.AppLogFilterMode))
        {
            RefreshAppLogRowHighlights();
        }
    }

    private void OnAppLogEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (subscribedViewModel?.IsAppLogPanelVisible == true && AppLogGrid.SelectedItem == null)
            ScrollAppLogToBottom();
    }

    // Resize the log panel by dragging the splitter on its top edge. Clamped so it stays
    // usable and never grows past most of the window. Session-only — not persisted.
    private void OnAppLogSplitterDragDelta(object? sender, VectorEventArgs e)
    {
        var max = Math.Max(160, Bounds.Height - 120);
        var newHeight = Math.Clamp(AppLogPanel.Height - e.Vector.Y, 100, max);
        AppLogPanel.Height = newHeight;
    }

    // Enter / F3 jumps to the next matching row in Search mode (Shift = previous).
    private void OnAppLogFilterBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (subscribedViewModel == null)
            return;

        if (e.Key is Key.Enter or Key.F3)
        {
            var backwards = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            JumpToNextAppLogMatch(backwards);
            e.Handled = true;
        }
    }

    private void OnAppLogFilterBoxTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (subscribedViewModel == null || subscribedViewModel.AppLogFilterMode)
            return;

        if (subscribedViewModel.AppLogFilterText.Length > 0)
        {
            JumpToFirstAppLogMatch();
        }
    }

    private void JumpToFirstAppLogMatch()
    {
        var vm = subscribedViewModel;
        if (vm == null || vm.AppLogFilterMode || vm.AppLogFilterText.Length == 0)
            return;

        var view = vm.AppLogView;
        var count = view.Count;
        if (count == 0)
            return;

        for (var i = 0; i < count; i++)
        {
            if (vm.MatchesAppLogFilter(view[i]))
            {
                AppLogGrid.SelectedIndex = i;
                AppLogGrid.ScrollIntoView(view[i], null);
                break;
            }
        }
    }

    // In Search mode every entry is present in the grid; move the selection to the next
    // (or previous) row that matches the filter and scroll it into view.
    private void JumpToNextAppLogMatch(bool backwards)
    {
        var vm = subscribedViewModel;
        if (vm == null || vm.AppLogFilterMode || vm.AppLogFilterText.Length == 0)
            return;

        var view = vm.AppLogView;
        var count = view.Count;
        if (count == 0)
            return;

        var start = AppLogGrid.SelectedIndex;
        var step = backwards ? -1 : 1;
        for (var i = 1; i <= count; i++)
        {
            var idx = ((start + step * i) % count + count) % count;
            if (vm.MatchesAppLogFilter(view[idx]))
            {
                AppLogGrid.SelectedIndex = idx;
                AppLogGrid.ScrollIntoView(view[idx], null);
                break;
            }
        }
    }

    // Tag rows that match the filter so the searchMatch style tints them. Only meaningful
    // in Search mode (in Filter mode the grid only contains matches).
    private void RefreshAppLogRowHighlights()
    {
        foreach (var row in AppLogGrid.GetVisualDescendants().OfType<DataGridRow>())
            ApplyAppLogRowHighlight(row);
    }

    private void ApplyAppLogRowHighlight(DataGridRow row)
    {
        var vm = subscribedViewModel;
        var isMatch = vm is { AppLogFilterMode: false }
                      && vm.AppLogFilterText.Length > 0
                      && row.DataContext is AppLogEntry entry
                      && vm.MatchesAppLogFilter(entry);

        if (isMatch != row.Classes.Contains("searchMatch"))
        {
            if (isMatch)
                row.Classes.Add("searchMatch");
            else
                row.Classes.Remove("searchMatch");
        }
    }

    private void OnAppLogGridLoadingRow(object? sender, DataGridRowEventArgs e)
    {
        ApplyAppLogRowHighlight(e.Row);
    }

    private void ScrollAppLogToBottom()
    {
        var entries = subscribedViewModel?.AppLogService.Entries;
        if (entries is not { Count: > 0 })
            return;

        var lastEntry = entries[^1];
        Dispatcher.UIThread.Post(() => AppLogGrid.ScrollIntoView(lastEntry, null));
    }
}