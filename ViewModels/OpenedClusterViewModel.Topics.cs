using System.ComponentModel;
using Avalonia.Threading;
using KafkaLens.Shared;
using KafkaLens.Shared.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using Serilog;

namespace KafkaLens.ViewModels;

public partial class OpenedClusterViewModel
{
    private bool isSyncingTopics;
    private bool suppressCheckedTopicFetch;

    [ObservableProperty] private string filterText = "";

    partial void OnFilterTextChanged(string value) => FilterTopics();

    public int CheckedTopicCount => Topics.Count(t => t.IsChecked);
    public bool HasCheckedTopics => Topics.Any(t => t.IsChecked);

    public string CheckedTopicsSummary => CheckedTopicCount == 1
        ? "1 topic selected"
        : $"{CheckedTopicCount} topics selected";

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

            foreach (var existing in Topics)
            {
                existing.PropertyChanged -= OnTopicPropertyChanged;
            }
            Topics.Clear();

            foreach (var topic in cluster.Topics)
            {
                var settings = topicSettingsService.GetSettings(cluster.Id, topic.Name);
                var valueFormatter = formatterService.NormalizeFormatterName(settings.ValueFormatter, ValueFormatterNames);
                var keyFormatter = formatterService.NormalizeFormatterName(settings.KeyFormatter, KeyFormatterNames);
                var viewModel = new TopicViewModel(topic, valueFormatter, keyFormatter);
                viewModel.PropertyChanged += OnTopicPropertyChanged;
                Topics.Add(viewModel);
            }

            NotifyCheckedTopicsChanged();
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

    private void OnTopicPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TopicViewModel.IsChecked)) return;
        if (sender is not TopicViewModel topic) return;

        OnTopicCheckedChanged(topic);
    }

    /// <summary>
    /// Checking a topic fetches just that topic and appends to the viewer; unchecking cancels
    /// its fetch and drops its rows. Only the transition into multi-topic mode clears the
    /// viewer, because the rows present then belong to the single selected node.
    /// </summary>
    private void OnTopicCheckedChanged(TopicViewModel topic)
    {
        NotifyCheckedTopicsChanged();

        if (suppressCheckedTopicFetch) return;

        if (topic.IsChecked)
        {
            if (CheckedTopicCount == 1)
            {
                CancelAllFetches();
                CurrentMessages.Clear();
                UseTopicFetchPositions();
            }

            if (IsCurrent) StartFetch(topic);
        }
        else
        {
            CancelFetch(topic.Name);
            CurrentMessages.RemoveTopic(topic.Name);
            UpdateLoadingState();
        }
    }

    /// <summary>
    /// A multi-topic fetch cannot use partition-only positions such as Offset.
    /// </summary>
    private void UseTopicFetchPositions()
    {
        FetchPositions = FetchPositionsForTopic;
        if (FetchPosition == null || !FetchPositionsForTopic.Contains(FetchPosition))
        {
            FetchPosition = FetchPositionsForTopic[0];
        }
    }

    internal void ClearCheckedTopics()
    {
        var checkedTopics = Topics.Where(t => t.IsChecked).ToList();
        if (checkedTopics.Count == 0) return;

        suppressCheckedTopicFetch = true;
        try
        {
            foreach (var topic in checkedTopics)
            {
                topic.IsChecked = false;
            }
        }
        finally
        {
            suppressCheckedTopicFetch = false;
        }

        CancelAllFetches();
        CurrentMessages.Clear();
        NotifyCheckedTopicsChanged();
        UpdateLoadingState();
    }

    private void NotifyCheckedTopicsChanged()
    {
        OnPropertyChanged(nameof(CheckedTopicCount));
        OnPropertyChanged(nameof(HasCheckedTopics));
        OnPropertyChanged(nameof(CheckedTopicsSummary));
        OnPropertyChanged(nameof(IsFetchOptionsEnabled));
    }
}
