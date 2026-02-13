using System.Collections.ObjectModel;
using KafkaLens.ViewModels.Search;

namespace KafkaLens.ViewModels;

public sealed class MessagesViewModel: ViewModelBase
{
    public ObservableCollection<MessageViewModel> Messages { get; } = new();
    public ObservableCollection<MessageViewModel> Filtered { get; } = new();

    public bool UseObjectFilter
    {
        get;
        set
        {
            if (SetProperty(ref field, value) && currentMessage != null)
            {
                currentMessage.UseObjectFilter = value;
            }
        }
    } = true;

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
                OnPropertyChanged(nameof(IsMessageSelected));
                if (currentMessage != null)
                {
                    currentMessage.LineFilter = lineFilter;
                    currentMessage.UseObjectFilter = UseObjectFilter;
                }
            }
        }
    }

    public bool IsMessageSelected => CurrentMessage != null;

    private string positiveFilter = "";
    private IFilterExpression positiveExpression = new AllMatchExpression();
    public string PositiveFilter
    {
        get => positiveFilter;
        set
        {
            if (positiveFilter == value)
                return;
            SetProperty(ref positiveFilter, value);
            positiveExpression = SearchParser.Parse(value);
            ApplyFilter();
        }
    }

    private string negativeFilter = "";
    private IFilterExpression negativeExpression = new NoneMatchExpression();
    public string NegativeFilter
    {
        get => negativeFilter;
        set
        {
            if (negativeFilter == value)
                return;
            SetProperty(ref negativeFilter, value);
            negativeExpression = SearchParser.Parse(value, false);
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
        return positiveExpression.Matches(message);
    }

    private bool NegativeFilterAccepts(string message)
    {
        return !negativeExpression.Matches(message);
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