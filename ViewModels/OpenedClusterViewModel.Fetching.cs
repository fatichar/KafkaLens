using System.Collections.Specialized;
using System.Threading;
using Avalonia.Threading;
using KafkaLens.Shared.Models;
using Serilog;

namespace KafkaLens.ViewModels;

public partial class OpenedClusterViewModel
{
    /// <summary>
    /// One in-flight fetch. A tab runs one of these per checked topic, so each stream needs
    /// its own formatter source and cancellation independent of the tree selection.
    /// </summary>
    private sealed class ActiveFetch
    {
        public required IMessageSource Source { get; init; }
        public required string TopicName { get; init; }
        public required int? PartitionId { get; init; }
        public required MessageStream Stream { get; init; }
        public required CancellationTokenSource Cts { get; init; }
        public required int RequestedCount { get; init; }
        public NotifyCollectionChangedEventHandler? MessagesChanged { get; set; }
        public MessageStream.FinishedEventHandler? Finished { get; set; }

        public string Description => PartitionId.HasValue
            ? $"topic {TopicName}, partition {PartitionId.Value}"
            : $"topic {TopicName}";

        public bool Targets(string topicName, int? partitionId) =>
            PartitionId == partitionId &&
            string.Equals(TopicName, topicName, StringComparison.Ordinal);
    }

    private readonly List<IMessageLoadListener> messageLoadListeners = new();
    private readonly List<MessageViewModel> pendingMessages = new();

    /// <summary>Mutated only on the UI thread.</summary>
    private readonly List<ActiveFetch> activeFetches = new();

    private void StopLoading()
    {
        if (IsLoading && activeFetches.Count > 0)
        {
            appLogService.LogInfo(
                $"Cancelled fetch from {DescribeTargets(activeFetches.Select(f => f.Description))}", "Fetch");
        }

        CancelAllFetches();
        IsLoading = false;
    }

    /// <summary>
    /// The topics/partitions the next fetch should read. Checked topics take priority; with
    /// none checked this falls back to the single tree-selected node.
    /// </summary>
    private List<IMessageSource> GetFetchTargets()
    {
        var checkedTopics = Topics.Where(t => t.IsChecked).ToList();
        if (checkedTopics.Count > 0)
        {
            return checkedTopics.Cast<IMessageSource>().ToList();
        }

        return selectedNode is IMessageSource source
            ? new List<IMessageSource> { source }
            : new List<IMessageSource>();
    }

    /// <summary>Clears the viewer and refetches every current target.</summary>
    private void FetchMessages()
    {
        var targets = GetFetchTargets();
        if (targets.Count == 0) return;

        CancelAllFetches();
        CurrentMessages.Clear();

        appLogService.LogInfo(
            $"Fetching {FetchCount} messages from {DescribeTargets(targets.Select(t => GetFetchDescription(t)))}",
            "Fetch");
        messageLoadListeners.ForEach(l => l.MessageLoadingStarted());

        IsLoading = true;
        foreach (var target in targets)
        {
            StartFetch(target);
        }

        UpdateLoadingState();
    }

    /// <summary>
    /// Starts a fetch for a single source and appends to the viewer without clearing it, so
    /// checking another topic adds to the existing rows instead of refetching everything.
    /// </summary>
    private void StartFetch(IMessageSource source)
    {
        var topicName = GetTopicName(source);
        if (topicName == null) return;

        var partitionId = source is PartitionViewModel partition ? partition.Id : (int?)null;
        CancelFetch(topicName, partitionId);

        // A fresh FetchOptions per target: Limit and Direction are mutable, so streams must
        // not share one instance.
        var fetchOptions = CreateFetchOptions();
        var cts = new CancellationTokenSource();
        MessageStream? stream;

        try
        {
            stream = partitionId.HasValue
                ? KafkaLensClient.GetMessageStream(cluster.Id, topicName, partitionId.Value, fetchOptions, cts.Token)
                : KafkaLensClient.GetMessageStream(cluster.Id, topicName, fetchOptions, cts.Token);
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to fetch messages for {ClusterName}", Name);
            appLogService.LogError($"Could not fetch messages from topic {topicName}: {e.Message}", "Fetch");
            UpdateLoadingState();
            return;
        }

        if (stream == null)
        {
            UpdateLoadingState();
            return;
        }

        var fetch = new ActiveFetch
        {
            Source = source,
            TopicName = topicName,
            PartitionId = partitionId,
            Stream = stream,
            Cts = cts,
            RequestedCount = fetchOptions.Limit
        };
        fetch.MessagesChanged = (_, e) => OnMessagesChanged(fetch, e);
        fetch.Finished = () => OnStreamFinished(fetch);

        activeFetches.Add(fetch);
        IsLoading = true;

        stream.Messages.CollectionChanged += fetch.MessagesChanged;
        stream.Finished += fetch.Finished;

        // A short stream can finish before the handler is attached, in which case Finished
        // never fires. OnStreamFinished is idempotent, so completing here is safe.
        if (!stream.HasMore) OnStreamFinished(fetch);
    }

    private void CancelFetch(string topicName, int? partitionId = null)
    {
        var existing = activeFetches.Where(f => f.Targets(topicName, partitionId)).ToList();
        foreach (var fetch in existing)
        {
            EndFetch(fetch);
        }
    }

    /// <summary>Cancels every in-flight fetch and drops messages that have not been shown yet.</summary>
    private void CancelAllFetches()
    {
        foreach (var fetch in activeFetches.ToList())
        {
            EndFetch(fetch);
        }

        lock (pendingMessages)
        {
            pendingMessages.Clear();
        }
    }

    private void EndFetch(ActiveFetch fetch)
    {
        if (fetch.MessagesChanged != null)
            fetch.Stream.Messages.CollectionChanged -= fetch.MessagesChanged;
        if (fetch.Finished != null)
            fetch.Stream.Finished -= fetch.Finished;

        activeFetches.Remove(fetch);

        lock (pendingMessages)
        {
            pendingMessages.RemoveAll(m => string.Equals(m.Topic, fetch.TopicName, StringComparison.Ordinal));
        }

        // Not disposed: the background stream task still holds the token, and disposing it
        // from under that task surfaces ObjectDisposedException inside the fetch pipeline.
        fetch.Cts.Cancel();
    }

    private void OnStreamFinished(ActiveFetch fetch)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            UpdateMessages();

            if (fetch.MessagesChanged != null)
                fetch.Stream.Messages.CollectionChanged -= fetch.MessagesChanged;
            if (fetch.Finished != null)
                fetch.Stream.Finished -= fetch.Finished;

            if (activeFetches.Remove(fetch))
            {
                appLogService.LogInfo(
                    $"Fetched {fetch.Stream.Messages.Count} of {fetch.RequestedCount} messages from {fetch.Description}",
                    "Fetch");
            }

            UpdateLoadingState();
        });
    }

    /// <summary>Loading stays true until every stream in the tab has finished.</summary>
    private void UpdateLoadingState()
    {
        if (activeFetches.Count > 0) return;

        IsLoading = false;
        messageLoadListeners.ForEach(l => l.MessageLoadingFinished());
    }

    private static string? GetTopicName(IMessageSource source) => source switch
    {
        TopicViewModel topic => topic.Name,
        PartitionViewModel partition => partition.TopicName,
        _ => null
    };

    private string GetCurrentTopicName() =>
        (selectedNode is IMessageSource source ? GetTopicName(source) : null)
        ?? throw new InvalidOperationException("No topic or partition is selected");

    /// <summary>
    /// Loaded messages belonging to one topic. The viewer can hold several topics at once, so
    /// per-topic operations such as formatter changes must not touch the other topics' rows.
    /// </summary>
    private IEnumerable<MessageViewModel> LoadedMessagesForTopic(string topicName) =>
        CurrentMessages.Messages.Where(m => string.Equals(m.Topic, topicName, StringComparison.Ordinal));

    private static string GetFetchDescription(ITreeNode node) => node switch
    {
        TopicViewModel topic => $"topic {topic.Name}",
        PartitionViewModel partition => $"topic {partition.TopicName}, partition {partition.Id}",
        _ => node.Name
    };

    private static string DescribeTargets(IEnumerable<string> descriptions)
    {
        var list = descriptions.ToList();
        return list.Count switch
        {
            0 => "no topics",
            1 => list[0],
            <= 3 => string.Join(", ", list),
            _ => $"{list.Count} topics"
        };
    }

    private void OnMessagesChanged(ActiveFetch fetch, NotifyCollectionChangedEventArgs e)
    {
        var node = fetch.Source;
        var topicName = fetch.TopicName;
        bool settingsChanged = false;

        if (formatterService.IsUnknownFormatter(node.FormatterName))
        {
            if (e.NewItems?.Count > 0)
            {
                var message = (Message)e.NewItems![0]!;
                var formatter = formatterService.GuessValueFormatter(message, ValueFormatterNames);
                node.FormatterName = formatter?.Name ?? formatterService.GetDefaultFormatterName();
            }
            settingsChanged = true;
            Log.Information("Guessed value formatter {Formatter} for topic {Topic}", node.FormatterName, topicName);
        }

        if (formatterService.IsUnknownFormatter(node.KeyFormatterName))
        {
            if (e.NewItems?.Count > 0)
            {
                var message = (Message)e.NewItems![0]!;
                var formatter = formatterService.GuessKeyFormatter(message, KeyFormatterNames);
                if (formatter != null)
                {
                    node.KeyFormatterName = formatter.Name;
                    settingsChanged = true;
                    Log.Information("Guessed key formatter {Formatter} for topic {Topic}", node.KeyFormatterName, topicName);
                }
            }
        }

        if (settingsChanged)
        {
            topicSettingsService.SetSettings(cluster.Id, topicName, new TopicSettings
            {
                KeyFormatter = node.KeyFormatterName,
                ValueFormatter = node.FormatterName
            });
        }

        lock (pendingMessages)
        {
            var valueFormatterName = formatterService.NormalizeFormatterName(node.FormatterName, ValueFormatterNames);
            var keyFormatterName = formatterService.NormalizeFormatterName(node.KeyFormatterName, KeyFormatterNames);
            foreach (var msg in e.NewItems ?? new List<Message>())
            {
                var viewModel = new MessageViewModel((Message)msg, valueFormatterName, keyFormatterName);
                viewModel.Topic = topicName;
                pendingMessages.Add(viewModel);
            }

            Dispatcher.UIThread.InvokeAsync(UpdateMessages);
        }
    }

    public void UpdateMessages()
    {
        lock (pendingMessages)
        {
            if (pendingMessages.Count > 0)
            {
                CurrentMessages.AddRange(pendingMessages);
                pendingMessages.Clear();
            }
        }
    }

    internal FetchOptions CreateFetchOptions()
    {
        FetchPosition start;
        FetchPosition? end = null;

        switch (FetchPosition)
        {
            case "End":
                end = Shared.Models.FetchPosition.End;
                start = new(PositionType.Offset, Shared.Models.FetchPosition.End.Offset - FetchCount);
                break;
            case "Start":
                start = Shared.Models.FetchPosition.Start;
                break;
            case "Timestamp":
                var epochMs = (long)(StartDateTime.ToUniversalTime() - new DateTime(1970, 1, 1)).TotalMilliseconds;
                start = new(PositionType.Timestamp, epochMs);
                break;
            case "Offset":
                start = new(PositionType.Offset, long.TryParse(StartOffset, out var offset) ? offset : -1);
                break;
            default:
                throw new Exception("Invalid fetch position " + FetchPosition);
        }

        return new FetchOptions(start, end)
        {
            Limit = FetchCount,
            Direction = FetchBackward ? FetchDirection.Backward : FetchDirection.Forward
        };
    }
}
