using Avalonia.Threading;
using KafkaLens.Shared;
using KafkaLens.Shared.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using Serilog;

namespace KafkaLens.ViewModels;

public partial class OpenedClusterViewModel
{
    private bool isSyncingTopics;

    [ObservableProperty] private string filterText = "";

    partial void OnFilterTextChanged(string value) => FilterTopics();

    internal async Task LoadTopicsAsync()
    {
        if (isSyncingTopics) return;
        isSyncingTopics = true;
        try
        {
            appLogService.LogInfo($"Loading topics for {Name}", "Topics");
            await cluster.EnsureTopicsLoadedAsync(forceRefresh: Topics.Count > 0);
            if (cluster.TopicLoadState == TopicLoadState.Failed && cluster.Topics.Count == 0)
            {
                return;
            }

            Topics.Clear();
            foreach (var topic in cluster.Topics)
            {
                var settings = topicSettingsService.GetSettings(cluster.Id, topic.Name);
                var valueFormatter = formatterService.NormalizeFormatterName(settings.ValueFormatter, ValueFormatterNames);
                var keyFormatter = formatterService.NormalizeFormatterName(settings.KeyFormatter, KeyFormatterNames);
                Topics.Add(new TopicViewModel(topic, valueFormatter, keyFormatter));
            }

            FilterTopics();
            RestorePendingSessionState();
            appLogService.LogInfo($"Loaded {Topics.Count} topics for {Name}", "Topics");
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to load topics for opened cluster {ClusterName}", Name);
        }
        finally
        {
            isSyncingTopics = false;
        }
    }

    internal void FilterTopics()
    {
        Children.Clear();
        foreach (var topic in Topics)
        {
            if (string.IsNullOrWhiteSpace(FilterText) ||
                topic.Name.Contains(FilterText, StringComparison.OrdinalIgnoreCase))
            {
                Children.Add(topic);
            }
        }
    }
}
