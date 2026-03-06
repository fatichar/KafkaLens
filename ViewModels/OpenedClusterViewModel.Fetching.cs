using System.Collections.Specialized;
using System.Threading;
using Avalonia.Threading;
using KafkaLens.Shared.Models;
using Serilog;
using Xunit;

namespace KafkaLens.ViewModels;

public partial class OpenedClusterViewModel
{
    private MessageStream? messages;
    private readonly List<IMessageLoadListener> messageLoadListeners = new();
    private readonly List<MessageViewModel> pendingMessages = new();
    private CancellationTokenSource? fetchCts;

    private void OnStreamFinished()
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            IsLoading = false;
            messageLoadListeners.ForEach(l => l.MessageLoadingFinished());
        });
    }

    private void StopLoading()
    {
        fetchCts?.Cancel();
        IsLoading = false;
    }

    private void FetchMessages()
    {
        if (selectedNode == null) return;

        fetchCts?.Cancel();
        fetchCts = new CancellationTokenSource();

        if (messages != null)
        {
            messages.Messages.CollectionChanged -= OnMessagesChanged;
            messages.Finished -= OnStreamFinished;
        }

        CurrentMessages.Clear();
        IsLoading = true;

        var fetchOptions = CreateFetchOptions();
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

    private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        var node = (IMessageSource?)SelectedNode;
        if (node == null) return;

        bool settingsChanged = false;
        var topicName = GetCurrentTopicName();

        if (formatterService.IsUnknownFormatter(node.FormatterName))
        {
            Assert.True(e.NewItems?.Count > 0);
            var message = (Message)e.NewItems![0]!;
            var formatter = formatterService.GuessValueFormatter(message, ValueFormatterNames);
            node.FormatterName = formatter?.Name ?? formatterService.GetDefaultFormatterName();
            settingsChanged = true;
            Log.Information("Guessed value formatter {Formatter} for topic {Topic}", node.FormatterName, topicName);
        }

        if (formatterService.IsUnknownFormatter(node.KeyFormatterName))
        {
            Assert.True(e.NewItems?.Count > 0);
            var message = (Message)e.NewItems![0]!;
            var formatter = formatterService.GuessKeyFormatter(message, KeyFormatterNames);
            if (formatter != null)
            {
                node.KeyFormatterName = formatter.Name;
                settingsChanged = true;
                Log.Information("Guessed key formatter {Formatter} for topic {Topic}", node.KeyFormatterName, topicName);
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
