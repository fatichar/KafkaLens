using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;

namespace KafkaLens.Shared.Utils;

public class ObservableRangeCollection<T> : ObservableCollection<T>
{
    public ObservableRangeCollection() : base() { }

    public ObservableRangeCollection(IEnumerable<T> collection) : base(collection) { }

    public void AddRange(IEnumerable<T> collection)
    {
        if (collection == null) throw new ArgumentNullException(nameof(collection));
        var list = collection.ToList();
        if (list.Count == 0) return;

        CheckReentrancy();

        int startIndex = Items.Count;
        foreach (var i in list) Items.Add(i);

        OnPropertyChanged(new PropertyChangedEventArgs("Count"));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, list, startIndex));
    }

    /// <summary>
    /// Removes every item matching <paramref name="match"/>, raising a single Reset
    /// notification instead of one event per removal.
    /// </summary>
    public int RemoveAll(Predicate<T> match)
    {
        if (match == null) throw new ArgumentNullException(nameof(match));

        CheckReentrancy();

        var kept = Items.Where(item => !match(item)).ToList();
        var removedCount = Items.Count - kept.Count;
        if (removedCount == 0) return 0;

        Items.Clear();
        foreach (var i in kept) Items.Add(i);

        OnPropertyChanged(new PropertyChangedEventArgs("Count"));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));

        return removedCount;
    }

    public void ReplaceRange(IEnumerable<T> collection)
    {
        if (collection == null) throw new ArgumentNullException(nameof(collection));
        var list = collection.ToList();

        CheckReentrancy();

        Items.Clear();
        foreach (var i in list) Items.Add(i);

        OnPropertyChanged(new PropertyChangedEventArgs("Count"));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}