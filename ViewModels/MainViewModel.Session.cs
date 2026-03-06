using System.Collections.Specialized;
using System.ComponentModel;
using KafkaLens.Shared.Models;
using KafkaLens.ViewModels.Messages;

namespace KafkaLens.ViewModels;

public partial class MainViewModel
{
    private readonly List<OpenedTabState> pendingRestoreTabs = new();
    private bool isRestoreStateInitialized;
    private readonly Dictionary<OpenedClusterViewModel, (PropertyChangedEventHandler TabHandler, PropertyChangedEventHandler MessagesHandler)>
        openedClusterStateHandlers = new();

    private async Task TryRestoreTabsAsync()
    {
        if (!isRestoreStateInitialized)
        {
            var config = settingsService.GetBrowserConfig();
            if (config.RestoreTabsOnStartup)
            {
                var tabsToRestore = config.OpenedTabs.Where(t => !string.IsNullOrWhiteSpace(t.ClusterId)).ToList();
                if (tabsToRestore.Count > 0 && await ConfirmRestoreTabs(tabsToRestore.Count))
                    pendingRestoreTabs.AddRange(tabsToRestore);
            }
            isRestoreStateInitialized = true;
        }

        if (pendingRestoreTabs.Count == 0) return;

        var remaining = new List<OpenedTabState>();
        foreach (var tab in pendingRestoreTabs)
        {
            var cluster = Clusters.FirstOrDefault(c => c.Id == tab.ClusterId);
            if (cluster != null)
                OpenCluster(cluster, tab);
            else
                remaining.Add(tab);
        }

        pendingRestoreTabs.Clear();
        pendingRestoreTabs.AddRange(remaining);
    }

    private void PersistOpenedTabsState()
    {
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
