using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using FluentAssertions;
using KafkaLens.Shared.Utils;
using Xunit;

namespace KafkaLens.Core.Tests.Utils;

public class ObservableRangeCollectionTests
{
    [Fact]
    public void Constructor_Default_IsEmpty()
    {
        // ReSharper disable once CollectionNeverUpdated.Local
        var collection = new ObservableRangeCollection<int>();
        collection.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_WithCollection_InitializesItems()
    {
        var items = new[] { 1, 2, 3 };
        var collection = new ObservableRangeCollection<int>(items);
        collection.Should().Equal(items);
    }

    [Fact]
    public void AddRange_AddsAllItems()
    {
        var collection = new ObservableRangeCollection<int>(new[] { 1 });
        var newItems = new[] { 2, 3 };

        collection.AddRange(newItems);

        collection.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void AddRange_FiresSingleNotification()
    {
        var collection = new ObservableRangeCollection<int>();
        var newItems = new[] { 1, 2, 3 };
        int callCount = 0;
        NotifyCollectionChangedEventArgs? eventArgs = null;

        collection.CollectionChanged += (s, e) =>
        {
            callCount++;
            eventArgs = e;
        };

        collection.AddRange(newItems);

        callCount.Should().Be(1);
        eventArgs.Should().NotBeNull();
        eventArgs!.Action.Should().Be(NotifyCollectionChangedAction.Add);
        eventArgs.NewItems.Should().BeEquivalentTo(newItems);
        eventArgs.NewStartingIndex.Should().Be(0);
    }

    [Fact]
    public void AddRange_EmptyCollection_DoesNotFireNotification()
    {
        var collection = new ObservableRangeCollection<int>();
        int callCount = 0;

        collection.CollectionChanged += (s, e) => callCount++;

        collection.AddRange(Enumerable.Empty<int>());

        callCount.Should().Be(0);
    }

    [Fact]
    public void AddRange_Null_ThrowsArgumentNullException()
    {
        var collection = new ObservableRangeCollection<int>();
        Action act = () => collection.AddRange(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ReplaceRange_ReplacesAllItems()
    {
        var collection = new ObservableRangeCollection<int>(new[] { 1, 2 });
        var newItems = new[] { 3, 4, 5 };

        collection.ReplaceRange(newItems);

        collection.Should().Equal(newItems);
    }

    [Fact]
    public void ReplaceRange_FiresResetNotification()
    {
        var collection = new ObservableRangeCollection<int>(new[] { 1, 2 });
        var newItems = new[] { 3, 4, 5 };
        int callCount = 0;
        NotifyCollectionChangedEventArgs? eventArgs = null;

        collection.CollectionChanged += (s, e) =>
        {
            callCount++;
            eventArgs = e;
        };

        collection.ReplaceRange(newItems);

        callCount.Should().Be(1);
        eventArgs.Should().NotBeNull();
        eventArgs!.Action.Should().Be(NotifyCollectionChangedAction.Reset);
    }

    [Fact]
    public void ReplaceRange_Null_ThrowsArgumentNullException()
    {
        var collection = new ObservableRangeCollection<int>();
        Action act = () => collection.ReplaceRange(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}