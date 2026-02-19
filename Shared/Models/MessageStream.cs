using KafkaLens.Shared.Utils;
using System.Collections.ObjectModel;

namespace KafkaLens.Shared.Models;

public sealed class MessageStream
{
    public ObservableRangeCollection<Message> Messages { get; set; } = new ObservableRangeCollection<Message>();

    public bool HasMore
    {
        get;
        set
        {
            field = value;
            if (!field)
            {
                Finished?.Invoke();
            }
        }
    } = true;

    public delegate void FinishedEventHandler();

    public event FinishedEventHandler? Finished;
}