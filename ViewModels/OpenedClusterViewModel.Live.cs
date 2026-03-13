using System.Collections.Specialized;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using KafkaLens.Shared.Models;
using Serilog;

namespace KafkaLens.ViewModels;

public partial class OpenedClusterViewModel
{
    private const int LiveIntervalSeconds = 5;

    [ObservableProperty] private bool isLiveMode;

    private DispatcherTimer? liveTimer;

    // Last seen offset per partition id. -1 means "not yet seen any message in this partition".
    private Dictionary<int, long> livePartitionOffsets = new();

    // Active live streams paired with their CollectionChanged handlers so we can unsubscribe.
    private readonly List<(MessageStream Stream, NotifyCollectionChangedEventHandler Handler)> liveStreams = new();

    // Separate CTS for live fetches so manual Fetch/Refresh doesn't cancel live streams.
    private CancellationTokenSource? liveFetchCts;

    partial void OnIsLiveModeChanged(bool value)
    {
        if (value)
            StartLiveMode();
        else
            StopLiveMode();
    }

    internal void StartLiveMode()
    {
        if (selectedNode == null)
        {
            IsLiveMode = false;
            return;
        }

        // Seed per-partition offsets from whatever is already displayed so we pick up exactly
        // where the current view left off — no duplicates, no gaps.
        livePartitionOffsets = CurrentMessages.Messages
            .GroupBy(m => m.Partition)
            .ToDictionary(g => g.Key, g => g.Max(m => m.Offset));

        liveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(LiveIntervalSeconds) };
        liveTimer.Tick += OnLiveTimerTick;
        liveTimer.Start();

        Log.Information("Live mode started for {Node}", selectedNode.Name);
    }

    internal void StopLiveMode()
    {
        liveTimer?.Stop();
        liveTimer = null;
        CancelAndClearLiveStreams();
        livePartitionOffsets.Clear();
        Log.Information("Live mode stopped");
    }

    private void OnLiveTimerTick(object? sender, EventArgs e)
    {
        if (!IsLiveMode || selectedNode == null)
        {
            IsLiveMode = false;
            return;
        }

        FetchLiveMessages();
    }

    private void FetchLiveMessages()
    {
        if (selectedNode == null) return;

        // Unsubscribe from previous live streams (they may still be producing — cancellation
        // stops the fetch loop, but we don't wait for it here).
        CancelAndClearLiveStreams();

        liveFetchCts = new CancellationTokenSource();

        foreach (var partition in GetLivePartitions())
        {
            // -1 means "no messages seen yet for this partition" — maps to watermarks.High via
            // WatermarkHelper (startOffset = High, limit clamped to 0), so the first poll
            // produces nothing and just marks our position at the current tip.
            // For subsequent polls, startOffset = lastOffset + 1 and limit is clamped to
            // exactly (High - startOffset), i.e. all new messages with no cap.
            var startOffset = livePartitionOffsets.TryGetValue(partition.Id, out var last)
                ? last + 1
                : -1L;

            var fetchOptions = new FetchOptions(
                new FetchPosition(PositionType.Offset, startOffset),
                end: null)
            {
                Limit = int.MaxValue,
                Direction = FetchDirection.Forward
            };

            var stream = KafkaLensClient.GetMessageStream(
                cluster.Id, partition.TopicName, partition.Id, fetchOptions, liveFetchCts.Token);

            var capturedPartitionId = partition.Id;
            NotifyCollectionChangedEventHandler handler =
                (_, args) => OnLiveMessagesArrived(args, capturedPartitionId);

            stream.Messages.CollectionChanged += handler;
            liveStreams.Add((stream, handler));
        }
    }

    private IEnumerable<PartitionViewModel> GetLivePartitions() => selectedNode switch
    {
        TopicViewModel topic => topic.Partitions,
        PartitionViewModel partition => new[] { partition },
        _ => Enumerable.Empty<PartitionViewModel>()
    };

    private void OnLiveMessagesArrived(NotifyCollectionChangedEventArgs e, int partitionId)
    {
        var node = (IMessageSource?)SelectedNode;
        if (node == null) return;

        lock (pendingMessages)
        {
            var valueFormatterName = formatterService.NormalizeFormatterName(node.FormatterName, ValueFormatterNames);
            var keyFormatterName = formatterService.NormalizeFormatterName(node.KeyFormatterName, KeyFormatterNames);
            var topicName = GetCurrentTopicName();

            foreach (Message msg in e.NewItems ?? (System.Collections.IList)Array.Empty<object>())
            {
                // Advance the per-partition bookmark.
                if (!livePartitionOffsets.TryGetValue(partitionId, out var current) || msg.Offset > current)
                    livePartitionOffsets[partitionId] = msg.Offset;

                pendingMessages.Add(new MessageViewModel(msg, valueFormatterName, keyFormatterName)
                {
                    Topic = topicName
                });
            }

            Dispatcher.UIThread.InvokeAsync(UpdateMessages);
        }
    }

    private void CancelAndClearLiveStreams()
    {
        liveFetchCts?.Cancel();
        liveFetchCts = null;

        foreach (var (stream, handler) in liveStreams)
            stream.Messages.CollectionChanged -= handler;

        liveStreams.Clear();
    }
}
