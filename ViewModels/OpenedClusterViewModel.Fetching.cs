using System.Collections.Specialized;
using System.Threading;
using Avalonia.Threading;
using KafkaLens.Shared.Models;
using Serilog;

namespace KafkaLens.ViewModels;

public partial class OpenedClusterViewModel
{
    private MessageStream? messages;
    private readonly List<IMessageLoadListener> messageLoadListeners = new();
    private readonly List<MessageViewModel> pendingMessages = new();
    private CancellationTokenSource? fetchCts;
    private string? activeFetchDescription;
    private int activeFetchRequestedCount;

    private MessageStream.FinishedEventHandler? streamFinishedHandler;

    private void OnStreamFinished(MessageStream finished, ClusterViewModel owner, string topic, int? partition, int version)
    {
        Dispatcher.UIThread.Post(() =>
        {
            // This stream is no longer the active one, so it owns no UI state at all.
            if (disposed || !ReferenceEquals(finished, messages)) return;

            // The fetch that owns the spinner has ended, so clear it regardless of whether the
            // result is still current. Deciding this behind the generation guard below would
            // strand the spinner forever whenever the cluster was invalidated mid-fetch.
            IsLoading = false;
            messageLoadListeners.ForEach(l => l.MessageLoadingFinished());

            // Status reporting is generation-scoped: a result from a superseded connection
            // must not overwrite the status of the current one.
            if (!ReferenceEquals(owner, cluster) || version != cluster.ConnectionVersion ||
                fetchCts?.IsCancellationRequested == true) return;
            if (finished.Error is { } error)
            {
                owner.ReportMessageResult(topic, partition, error, version);
                appLogService.LogError($"Could not fetch messages from {activeFetchDescription}: {error.Message}", "Fetch");
            }
            else if (!finished.WasCanceled)
            {
                owner.ReportMessageResult(topic, partition, null, version);
                appLogService.LogInfo(
                    $"Fetched {finished.Messages.Count} of {activeFetchRequestedCount} messages from {activeFetchDescription}",
                    "Fetch");
            }
        });
    }

    private void DetachMessageStream()
    {
        if (messages == null) return;
        messages.Messages.CollectionChanged -= OnMessagesChanged;
        messages.Finished -= streamFinishedHandler;
        streamFinishedHandler = null;
        messages = null;
        lock (pendingMessages) pendingMessages.Clear();
    }

    private void StopLoading()
    {
        if (IsLoading && activeFetchDescription != null)
            appLogService.LogInfo($"Cancelled fetch from {activeFetchDescription}", "Fetch");
        fetchCts?.Cancel();
        IsLoading = false;
    }

    private void FetchMessages()
    {
        if (disposed || !cluster.IsAvailable || selectedNode is not (TopicViewModel or PartitionViewModel)) return;

        fetchCts?.Cancel();
        fetchCts?.Dispose();
        fetchCts = new CancellationTokenSource();
        DetachMessageStream();
        var owner = cluster;
        var version = owner.ConnectionVersion;
        var topicName = GetCurrentTopicName();
        int? partitionId = (selectedNode as PartitionViewModel)?.Id;

        CurrentMessages.Clear();
        IsLoading = true;

        var fetchOptions = CreateFetchOptions();
        activeFetchDescription = GetFetchDescription(selectedNode);
        activeFetchRequestedCount = fetchOptions.Limit;
        appLogService.LogInfo(
            $"Fetching {fetchOptions.Limit} messages from {activeFetchDescription}",
            "Fetch");
        messageLoadListeners.ForEach(l => l.MessageLoadingStarted());

        try
        {
            messages = selectedNode switch
            {
                TopicViewModel topic => KafkaLensClient.GetMessageStream(
                    cluster.Id, topic.Name, fetchOptions, fetchCts.Token),
                PartitionViewModel partition => KafkaLensClient.GetMessageStream(
                    cluster.Id, partition.TopicName, partition.Id, fetchOptions, fetchCts.Token),
                _ => null
            };
        }
        catch (Exception e)
        {
            IsLoading = false;
            if (e is not OperationCanceledException)
            {
                owner.ReportMessageResult(topicName, partitionId, e, version);
            }
            Log.Error(e, "Failed to fetch messages for {ClusterName}", Name);
            appLogService.LogError($"Could not fetch messages from {activeFetchDescription}: {e.Message}", "Fetch");
            return;
        }

        if (messages != null)
        {
            var stream = messages;
            var completed = 0;
            streamFinishedHandler = () =>
            {
                if (Interlocked.Exchange(ref completed, 1) == 0)
                    OnStreamFinished(stream, owner, topicName, partitionId, version);
            };
            stream.Messages.CollectionChanged += OnMessagesChanged;
            stream.Finished += streamFinishedHandler;
            if (!stream.HasMore) streamFinishedHandler();
        }
    }

    private static string GetFetchDescription(ITreeNode node) => node switch
    {
        TopicViewModel topic => $"topic {topic.Name}",
        PartitionViewModel partition => $"topic {partition.TopicName}, partition {partition.Id}",
        _ => node.Name
    };

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            if (e.NewItems == null) return;
            var items = e.NewItems.Cast<Message>().ToArray();
            Dispatcher.UIThread.Post(() => OnMessagesChanged(sender,
                new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, items)));
            return;
        }
        if (disposed || !ReferenceEquals(sender, messages?.Messages) || fetchCts?.IsCancellationRequested == true) return;
        var node = (IMessageSource?)SelectedNode;
        if (node == null) return;

        bool settingsChanged = false;
        var topicName = GetCurrentTopicName();

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

    private string GetCurrentTopicName() => selectedNode switch
    {
        TopicViewModel topic => topic.Name,
        PartitionViewModel partition => partition.TopicName,
        _ => throw new InvalidOperationException()
    };

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

        if (!messages?.HasMore ?? false)
            IsLoading = false;
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
