using System.Linq;
using System.ComponentModel;
using Avalonia.Threading;
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

    private bool filterRefreshQueued;

    private void OnMessageChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(MessageViewModel.DecodedMessage) ||
            (string.IsNullOrWhiteSpace(positiveFilter) && string.IsNullOrWhiteSpace(negativeFilter)) ||
            filterRefreshQueued)
            return;

        filterRefreshQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            filterRefreshQueued = false;
            ApplyFilter();
        });
    }

    private void ApplyFilter()
    {
        var filtered = Messages.Where(m => FilterAccepts(m.DecodedMessage)).ToList();
        Filtered.ReplaceRange(filtered);
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
        foreach (var message in Messages)
        {
            message.PropertyChanged -= OnMessageChanged;
            message.CancelRegistryDecodes();
        }
        Messages.Clear();
        Filtered.Clear();
        CurrentMessage = null;
    }

    internal void Add(MessageViewModel message)
    {
        message.PropertyChanged += OnMessageChanged;
        Messages.Add(message);
        if (FilterAccepts(message.DecodedMessage))
        {
            Filtered.Add(message);
        }
    }

    internal void AddRange(IEnumerable<MessageViewModel> messages)
    {
        var list = messages.ToList();
        if (list.Count == 0) return;

        foreach (var message in list) message.PropertyChanged += OnMessageChanged;
        Messages.AddRange(list);

        var filteredList = list.Where(m => FilterAccepts(m.DecodedMessage)).ToList();
        if (filteredList.Count > 0)
        {
            Filtered.AddRange(filteredList);
        }
    }
}
