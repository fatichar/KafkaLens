using KafkaLens.Shared.Utils;

namespace KafkaLens.Shared.Models;

public sealed class MessageStream
{
    public ObservableRangeCollection<Message> Messages { get; set; } = new ObservableRangeCollection<Message>();
    private bool hasMore = true;

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
