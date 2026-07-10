using System.Collections.Specialized;
using System.ComponentModel;
using KafkaLens.Shared.Models;
using KafkaLens.ViewModels.Messages;

namespace KafkaLens.ViewModels;

public partial class MainViewModel
{
    private readonly List<OpenedTabState> pendingRestoreTabs = new();
    private Task? restoreStateInitTask;
    private bool restoreDecisionPending = true;
    private readonly Dictionary<OpenedClusterViewModel, (PropertyChangedEventHandler TabHandler, PropertyChangedEventHandler MessagesHandler)>
        openedClusterStateHandlers = new();

    private async Task TryRestoreTabsAsync()
    {
        restoreStateInitTask ??= InitializeRestoreStateAsync();
        await restoreStateInitTask;

        if (pendingRestoreTabs.Count == 0) return;

        // Tabs the user may have opened manually while the restore-tabs confirmation
        // was pending. Skip re-opening them so restoring doesn't duplicate them.
        var alreadyOpenCounts = OpenedClusters
            .GroupBy(c => c.ClusterId)
            .ToDictionary(g => g.Key, g => g.Count());

        var remaining = new List<OpenedTabState>();
        foreach (var tab in pendingRestoreTabs)
        {
            if (alreadyOpenCounts.TryGetValue(tab.ClusterId, out var count) && count > 0)
            {
                alreadyOpenCounts[tab.ClusterId] = count - 1;
                continue;
            }

            var cluster = Clusters.FirstOrDefault(c => c.Id == tab.ClusterId);
            if (cluster != null)
                OpenCluster(cluster, tab);
            else if (!string.IsNullOrWhiteSpace(tab.SavedMessagesPath))
                await OpenSavedMessagesAsync(tab.SavedMessagesPath, tab);
            else
                remaining.Add(tab);
        }

        pendingRestoreTabs.Clear();
        pendingRestoreTabs.AddRange(remaining);
    }

    private async Task InitializeRestoreStateAsync()
    {
        try
        {
            var config = settingsService.GetBrowserConfig();
            if (config.RestoreTabsOnStartup)
            {
                var tabsToRestore = config.OpenedTabs.Where(t => !string.IsNullOrWhiteSpace(t.ClusterId)).ToList();
                if (tabsToRestore.Count > 0 && await ConfirmRestoreTabs(tabsToRestore.Count))
                    pendingRestoreTabs.AddRange(tabsToRestore);
            }
        }
        finally
        {
            restoreDecisionPending = false;
        }
    }

    private void PersistOpenedTabsState()
    {
        // Avoid clobbering the previous session's saved tabs on disk while the
        // restore-tabs confirmation is still pending (the user may have already
        // opened a tab manually before answering the dialog).
        if (restoreDecisionPending) return;

        var config = settingsService.GetBrowserConfig();
        if (!config.RestoreTabsOnStartup) return;

        config.OpenedTabs = OpenedClusters.Select(c => c.CaptureOpenedTabState()).ToList();
        settingsService.SaveBrowserConfig(config);
    }

    private void OnOpenedClustersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e?.OldItems != null)
            foreach (OpenedClusterViewModel opened in e.OldItems)
                UnsubscribeFromOpenedClusterState(opened);

        if (e?.NewItems != null)
            foreach (OpenedClusterViewModel opened in e.NewItems)
                SubscribeToOpenedClusterState(opened);

        PersistOpenedTabsState();
    }

    private void SubscribeToOpenedClusterState(OpenedClusterViewModel opened)
    {
        if (openedClusterStateHandlers.ContainsKey(opened)) return;

        PropertyChangedEventHandler tabHandler = (_, args) =>
        {
            if (args.PropertyName is
                nameof(OpenedClusterViewModel.MessagesSortColumn) or
                nameof(OpenedClusterViewModel.MessagesSortAscending) or
                nameof(OpenedClusterViewModel.SelectedNode) or
                nameof(OpenedClusterViewModel.FetchPosition) or
                nameof(OpenedClusterViewModel.FetchCount) or
                nameof(OpenedClusterViewModel.FetchBackward) or
                nameof(OpenedClusterViewModel.StartOffset) or
                nameof(OpenedClusterViewModel.StartDate) or
                nameof(OpenedClusterViewModel.StartTimeText))
            {
                PersistOpenedTabsState();
            }
        };

        PropertyChangedEventHandler messagesHandler = (_, args) =>
        {
            if (args.PropertyName is
                nameof(MessagesViewModel.PositiveFilter) or
                nameof(MessagesViewModel.NegativeFilter) or
                nameof(MessagesViewModel.LineFilter) or
                nameof(MessagesViewModel.UseObjectFilter))
            {
                PersistOpenedTabsState();
            }
        };

        opened.PropertyChanged += tabHandler;
        opened.CurrentMessages.PropertyChanged += messagesHandler;
        openedClusterStateHandlers[opened] = (tabHandler, messagesHandler);
    }

    private void UnsubscribeFromOpenedClusterState(OpenedClusterViewModel opened)
    {
        if (!openedClusterStateHandlers.TryGetValue(opened, out var handlers)) return;
        opened.PropertyChanged -= handlers.TabHandler;
        opened.CurrentMessages.PropertyChanged -= handlers.MessagesHandler;
        openedClusterStateHandlers.Remove(opened);
    }
}
