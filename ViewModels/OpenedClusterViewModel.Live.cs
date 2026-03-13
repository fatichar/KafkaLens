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
    private long lastLiveEpochMs;

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

        // Seed timestamp so the first poll fetches messages from the recent window
        lastLiveEpochMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - LiveIntervalSeconds * 1000L;

        liveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(LiveIntervalSeconds) };
        liveTimer.Tick += OnLiveTimerTick;
        liveTimer.Start();

        Log.Information("Live mode started for {Node}", selectedNode.Name);
    }

    internal void StopLiveMode()
    {
        liveTimer?.Stop();
        liveTimer = null;
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

        fetchCts?.Cancel();
        fetchCts = new CancellationTokenSource();

        if (messages != null)
        {
            messages.Messages.CollectionChanged -= OnMessagesChanged;
            messages.Finished -= OnStreamFinished;
        }

        // Append-only: do not clear CurrentMessages
        IsLoading = true;

        var capturedEpochMs = lastLiveEpochMs;
        lastLiveEpochMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var fetchOptions = new FetchOptions(
            new FetchPosition(PositionType.Timestamp, capturedEpochMs),
            end: null)
        {
            Limit = FetchCount,
            Direction = FetchDirection.Forward
        };

        messageLoadListeners.ForEach(l => l.MessageLoadingStarted());

        messages = selectedNode switch
        {
            TopicViewModel topic => KafkaLensClient.GetMessageStream(
                cluster.Id, topic.Name, fetchOptions, fetchCts.Token),
            PartitionViewModel partition => KafkaLensClient.GetMessageStream(
                cluster.Id, partition.TopicName, partition.Id, fetchOptions, fetchCts.Token),
            _ => null
        };

        if (messages != null)
        {
            messages.Messages.CollectionChanged += OnMessagesChanged;
            messages.Finished += OnStreamFinished;
        }
    }
}
