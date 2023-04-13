using System.Collections.ObjectModel;

namespace KafkaLens.ViewModels;

public sealed class MessagesViewModel: ViewModelBase
{
    private StringComparison comparisonType = StringComparison.OrdinalIgnoreCase;
    public ObservableCollection<MessageViewModel> Messages { get; } = new();
    public ObservableCollection<MessageViewModel> Filtered { get; } = new();

    public ObservableCollection<MessageViewModel> SelectedMessages { get; } = new();

    private MessageViewModel currentMessage;
    public MessageViewModel CurrentMessage
    {
        get => currentMessage;
        set
        {
            if (SetProperty(ref currentMessage, value))
            {
                if (currentMessage != null)
                {
                    currentMessage.LineFilter = lineFilter;
                }
            }
        }
    }

    private int selectedIndex;
    public int SelectedIndex
    {
        get => selectedIndex;
        set
        {
            if (value < 0)
            {
                return;
            }
            if (SetProperty(ref selectedIndex, value))
            {
                if (selectedIndex >= 0 && selectedIndex < Filtered.Count)
                {
                    CurrentMessage = Filtered[selectedIndex];
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