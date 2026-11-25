using Avalonia.Threading;
using KafkaLens.Shared;
using KafkaLens.Formatting;
using KafkaLens.Shared.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using Serilog;
using KafkaLens.ViewModels.Telemetry;

namespace KafkaLens.ViewModels;

public partial class OpenedClusterViewModel
{
    private bool isSyncingTopics;
    private bool isRetryingTopics;
    private bool topicsAvailabilityTracked;

    [ObservableProperty] private string filterText = "";

    partial void OnFilterTextChanged(string value)
    {
        TrackTopicFilter();
        FilterTopics();
    }

    private async Task ConnectAsync()
    {
        if (cluster.IsChecking) return;

        if (disposed || !cluster.IsAvailable) return;
        isRetryingTopics = true;
        try
        {
            await cluster.RecheckConnectionAsync();
            if (cluster.HasMessageFailures && selectedNode is TopicViewModel or PartitionViewModel)
                FetchMessages();
            else
                SyncTopics("retry");
        }
        finally
        {
            isRetryingTopics = false;
        }
    }

    internal async Task LoadTopicsAsync()
    {
        if (isSyncingTopics) return;
        isSyncingTopics = true;
        try
        {
            appLogService.LogInfo($"Loading topics for {Name}", "Topics");
            await cluster.EnsureTopicsLoadedAsync(forceRefresh: Topics.Count > 0);
            var failed = cluster.TopicLoadState == TopicLoadState.Failed && cluster.Topics.Count == 0;
            if (failed)
            {
                return;
            }

            SyncTopics("initial");
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

    private void SyncTopics(string source)
    {
        if (disposed || cluster.TopicLoadState != TopicLoadState.Loaded) return;
        Topics.Clear();
        var registryDefault = GetRegistryDefault();
        foreach (var topic in cluster.Topics)
        {
            var settings = topicSettingsService.GetSettings(cluster.Id, topic.Name);
            var valueFormatter = formatterService.NormalizeFormatterName(settings.ValueFormatter ?? registryDefault, ValueFormatterNames);
            var keyFormatter = formatterService.NormalizeFormatterName(settings.KeyFormatter ?? registryDefault, KeyFormatterNames);
            Topics.Add(new TopicViewModel(topic, valueFormatter, keyFormatter));
        }
        FilterTopics();
        if (!topicsAvailabilityTracked)
        {
            topicsAvailabilityTracked = true;
            telemetry.Track(TelemetrySchema.Events.TopicsAvailable, new Dictionary<string, object?> { ["source"] = source });
        }
        RestorePendingSessionState();
    }

    private string? GetRegistryDefault() =>
        string.IsNullOrWhiteSpace(cluster.EffectiveSchemaRegistryUrl) ? null : SchemaRegistryFormatter.Name;

    private void UpdateSchemaRegistryFormatters()
    {
        var registryDefault = GetRegistryDefault();
        foreach (var topic in Topics)
        {
            var settings = topicSettingsService.GetSettings(cluster.Id, topic.Name);
            if (settings.ValueFormatter == null && (formatterService.IsUnknownFormatter(topic.FormatterName) ||
                topic.FormatterName == SchemaRegistryFormatter.Name))
            {
                topic.FormatterName = formatterService.NormalizeFormatterName(registryDefault, ValueFormatterNames);
            }
            if (settings.KeyFormatter == null && (formatterService.IsUnknownFormatter(topic.KeyFormatterName) ||
                topic.KeyFormatterName == SchemaRegistryFormatter.Name))
            {
                topic.KeyFormatterName = formatterService.NormalizeFormatterName(registryDefault, KeyFormatterNames);
            }
        }

        foreach (var message in CurrentMessages.Messages)
            message.UpdateSchemaRegistry(cluster.EffectiveSchemaRegistry);
        lock (pendingMessages)
            foreach (var message in pendingMessages)
                message.UpdateSchemaRegistry(cluster.EffectiveSchemaRegistry);
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
