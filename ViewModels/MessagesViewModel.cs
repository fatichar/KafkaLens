using System.Linq;
﻿using KafkaLens.ViewModels.Search;
using KafkaLens.Shared.Utils;

namespace KafkaLens.ViewModels;

public sealed class MessagesViewModel: ViewModelBase
{
    public ObservableRangeCollection<MessageViewModel> Messages { get; } = new();
    public ObservableRangeCollection<MessageViewModel> Filtered { get; } = new();

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
            if (!SetProperty(ref lineFilter, value))
            {
                return;
            }

            if (currentMessage != null)
            {
                currentMessage.LineFilter = lineFilter;
            }
        }
    }

    private void ApplyFilter()
    {
        var filtered = Messages.Where(FilterAccepts).ToList();
        Filtered.ReplaceRange(filtered);
    }

    private bool FilterAccepts(MessageViewModel message)
    {
        return NegativeFilterAccepts(message)
               && PositiveFilterAccepts(message);
    }

    private bool PositiveFilterAccepts(MessageViewModel message)
    {
        return positiveExpression.Matches(message);
    }

    private bool NegativeFilterAccepts(MessageViewModel message)
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
        if (FilterAccepts(message))
        {
            Filtered.Add(message);
        }
    }

    internal void AddRange(IEnumerable<MessageViewModel> messages)
    {
        var list = messages.ToList();
        if (list.Count == 0) return;

        Messages.AddRange(list);

        var filteredList = list.Where(FilterAccepts).ToList();
        if (filteredList.Count > 0)
        {
            Filtered.AddRange(filteredList);
        }
    }

    /// <summary>
    /// Drops every message belonging to <paramref name="topicName"/>. Used when a topic is
    /// unchecked so its rows leave the viewer without disturbing the other topics.
    /// </summary>
    internal void RemoveTopic(string topicName)
    {
        Predicate<MessageViewModel> belongsToTopic = m =>
            string.Equals(m.Topic, topicName, StringComparison.Ordinal);

        var selectionRemoved = currentMessage != null && belongsToTopic(currentMessage);

        Messages.RemoveAll(belongsToTopic);
        Filtered.RemoveAll(belongsToTopic);

        // Cleared after removal: the CurrentMessage setter ignores null while the
        // selection is still present in Filtered.
        if (selectionRemoved) CurrentMessage = null;
    }
}
