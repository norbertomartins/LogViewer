using System.Collections.Specialized;
using LogViewer.App.Controls;
using LogViewer.App.Models;

namespace LogViewer.App.Tests.Controls;

public sealed class DisplayLineCollectionTests
{
    [Fact]
    public void AppendRange_AddsItems_AndRaisesASingleResetNotification()
    {
        var collection = new DisplayLineCollection(capacity: 10);
        var resets = 0;
        collection.CollectionChanged += (_, e) =>
        {
            Assert.Equal(NotifyCollectionChangedAction.Reset, e.Action);
            resets++;
        };

        collection.AppendRange([Line(1), Line(2), Line(3)]);

        Assert.Equal(3, collection.Count);
        Assert.Equal(1, resets);
    }

    [Fact]
    public void AppendRange_WithEmptyList_IsANoOp()
    {
        var collection = new DisplayLineCollection(capacity: 10);
        var raised = false;
        collection.CollectionChanged += (_, _) => raised = true;

        collection.AppendRange([]);

        Assert.Empty(collection);
        Assert.False(raised, "An empty AppendRange should not raise CollectionChanged.");
    }

    [Fact]
    public void AppendRange_BeyondCapacity_EvictsTheOldestLinesFifo()
    {
        var collection = new DisplayLineCollection(capacity: 3);

        collection.AppendRange([Line(1), Line(2), Line(3)]);
        collection.AppendRange([Line(4), Line(5)]);

        Assert.Equal(3, collection.Count);
        Assert.Equal([3L, 4L, 5L], collection.Select(l => l.LineNumber).ToArray());
    }

    [Fact]
    public void FindByLineNumber_ReturnsTheLine_WhileItIsStillRetained()
    {
        var collection = new DisplayLineCollection(capacity: 10);
        collection.AppendRange([Line(1), Line(2), Line(3)]);

        var found = collection.FindByLineNumber(2);

        Assert.NotNull(found);
        Assert.Equal(2, found.LineNumber);
    }

    [Fact]
    public void FindByLineNumber_ReturnsNull_OnceTheLineHasBeenEvicted()
    {
        var collection = new DisplayLineCollection(capacity: 2);
        collection.AppendRange([Line(1), Line(2)]);

        Assert.NotNull(collection.FindByLineNumber(1));

        collection.AppendRange([Line(3)]);

        Assert.Null(collection.FindByLineNumber(1));
        Assert.NotNull(collection.FindByLineNumber(2));
        Assert.NotNull(collection.FindByLineNumber(3));
    }

    [Fact]
    public void FindByLineNumber_ReturnsNull_ForALineNumberNeverAdded()
    {
        var collection = new DisplayLineCollection(capacity: 10);
        collection.AppendRange([Line(1)]);

        Assert.Null(collection.FindByLineNumber(999));
    }

    [Fact]
    public void Clear_EmptiesTheCollection_AndRaisesReset()
    {
        var collection = new DisplayLineCollection(capacity: 10);
        collection.AppendRange([Line(1), Line(2)]);

        var resets = 0;
        collection.CollectionChanged += (_, _) => resets++;
        collection.Clear();

        Assert.Empty(collection);
        Assert.Null(collection.FindByLineNumber(1));
        Assert.Equal(1, resets);
    }

    [Fact]
    public void Clear_OnAnAlreadyEmptyCollection_IsANoOp()
    {
        var collection = new DisplayLineCollection(capacity: 10);
        var raised = false;
        collection.CollectionChanged += (_, _) => raised = true;

        collection.Clear();

        Assert.False(raised);
    }

    private static LogLineViewModel Line(long number) => new(number, $"line {number}", null, null, isBookmarked: false);
}
