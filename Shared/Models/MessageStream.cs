using System;
using KafkaLens.Shared.Utils;

namespace KafkaLens.Shared.Models;

public sealed class MessageStream
{
    public ObservableRangeCollection<Message> Messages { get; set; } = new ObservableRangeCollection<Message>();
    public Exception? Error { get; private set; }
    public bool WasCanceled { get; private set; }
    private bool hasMore = true;

    public void SetError(Exception error)
    {
        Error = error;
    }

    public void SetCanceled()
    {
        WasCanceled = true;
    }

    public bool HasMore
    {
        get => hasMore;
        set
        {
            if (hasMore == value)
            {
                return;
            }

            hasMore = value;
            if (!hasMore)
            {
                Finished?.Invoke();
            }
        }
    }

    public delegate void FinishedEventHandler();

    public event FinishedEventHandler? Finished;
}
