using System.Collections.ObjectModel;

namespace KafkaLens.ViewModels;

public sealed class MessagesViewModel: ViewModelBase
{
    private readonly StringComparison comparisonType = StringComparison.OrdinalIgnoreCase;
    public ObservableCollection<MessageViewModel> Messages { get; } = new();
    public ObservableCollection<MessageViewModel> Filtered { get; } = new();

    private bool useObjectFilter = true;
    public bool UseObjectFilter
    {
        get => useObjectFilter;
        set
        {
            if (SetProperty(ref useObjectFilter, value) && currentMessage != null)
            {
                currentMessage.UseObjectFilter = value;
            }
        }
    }

    private MessageViewModel? currentMessage;
    public MessageViewModel? CurrentMessage
    {
        get => currentMessage;
        set
        {
            // Ignore null when there's already a selection and messages exist
            // This prevents tab switching from clearing the selection
            if (value == null && currentMessage != null && Filtered.Contains(currentMessage))
            {
                return;
            }

            if (currentMessage != null && currentMessage != value)
            {
                currentMessage.Cleanup();
            }
            if (SetProperty(ref currentMessage, value))
            {
                if (currentMessage != null)
                {
                    currentMessage.LineFilter = lineFilter;
                    currentMessage.UseObjectFilter = UseObjectFilter;
                }
            }
        }
    }

    private string positiveFilter = "";
    public string PositiveFilter
    {
        get => positiveFilter;
        set
        {
            if (positiveFilter == value)
                return;
            SetProperty(ref positiveFilter, value);
            ApplyFilter();
        }
    }

    private string negativeFilter = "";
    public string NegativeFilter
    {
        get => negativeFilter;
        set
        {
            if (negativeFilter == value)
                return;
            SetProperty(ref negativeFilter, value);
            ApplyFilter();
        }
    }

    private string lineFilter = "";

    public string LineFilter
    {
        get => lineFilter;
        set
        {
            lineFilter = value;
            if (currentMessage != null)
            {
                currentMessage.LineFilter = lineFilter;
            }
        }
    }

    private void ApplyFilter()
    {
        Filtered.Clear();

        foreach (var message in Messages)
        {
            if (FilterAccepts(message.DecodedMessage))
            {
                Filtered.Add(message);
            }
        }
    }

    private bool FilterAccepts(string message)
    {
        return NegativeFilterAccepts(message)
               && PositiveFilterAccepts(message);
    }

    private bool PositiveFilterAccepts(string message)
    {
        if (string.IsNullOrEmpty(positiveFilter))
        {
            return true;
        }
        return message.Contains(PositiveFilter, comparisonType);
    }

    private bool NegativeFilterAccepts(string message)
    {
        if (string.IsNullOrEmpty(negativeFilter))
        {
            return true;
        }
        return !message.Contains(NegativeFilter, comparisonType);
    }

    internal void Clear()
    {
        Messages.Clear();
        Filtered.Clear();
        CurrentMessage = null;
    }

    internal void Add(MessageViewModel message)
    {
        Messages.Add(message);
        if (FilterAccepts(message.DecodedMessage))
        {
            Filtered.Add(message);
        }
    }
}