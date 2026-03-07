using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
    private CancellationTokenSource? filterCts;

    public string PositiveFilter
    {
        get => positiveFilter;
        set
        {
            if (positiveFilter == value)
                return;
            SetProperty(ref positiveFilter, value);
            positiveExpression = SearchParser.Parse(value);
            _ = ApplyFilterAsync();
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
            _ = ApplyFilterAsync();
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

    private async Task ApplyFilterAsync()
    {
        filterCts?.Cancel();
        filterCts = new CancellationTokenSource();
        var token = filterCts.Token;

        var messagesCopy = Messages.ToList();

        try
        {
            var filteredMessages = await Task.Run(() =>
            {
                var result = new List<MessageViewModel>();
                foreach (var message in messagesCopy)
                {
                    token.ThrowIfCancellationRequested();
                    if (FilterAccepts(message.DecodedMessage))
                    {
                        result.Add(message);
                    }
                }
                return result;
            }, token);

            if (!token.IsCancellationRequested)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                {
                    if (!token.IsCancellationRequested)
                    {
                        Filtered.ReplaceRange(filteredMessages);
                    }
                });
            }
        }
        catch (OperationCanceledException)
        {
            // Ignore cancellation
        }
    }

    private void ApplyFilter()
    {
        _ = ApplyFilterAsync();
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

    internal void AddRange(IEnumerable<MessageViewModel> messages)
    {
        var list = messages.ToList();
        if (list.Count == 0) return;

        Messages.AddRange(list);

        var filteredList = list.Where(m => FilterAccepts(m.DecodedMessage)).ToList();
        if (filteredList.Count > 0)
        {
            Filtered.AddRange(filteredList);
        }
    }
}
