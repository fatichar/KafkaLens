using System.Collections.ObjectModel;

namespace KafkaLens.Shared.Models;

public sealed class MessageStream
{
    public ObservableCollection<Message> Messages { get; set; } = new ObservableCollection<Message>();

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