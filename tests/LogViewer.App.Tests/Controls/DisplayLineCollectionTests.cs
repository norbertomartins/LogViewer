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

    [Fact]
    public void FindByLineNumber_AndIndexOf_StayCorrect_AcrossManyEvictions()
    {
        var collection = new DisplayLineCollection(capacity: 3);
        for (var n = 1; n <= 10; n += 2)
        {
            collection.AppendRange([Line(n), Line(n + 1)]);
        }

        Assert.Equal([8L, 9L, 10L], collection.Select(l => l.LineNumber).ToArray());
        Assert.Null(collection.FindByLineNumber(7));
        for (var i = 0; i < collection.Count; i++)
        {
            Assert.Same(collection[i], collection.FindByLineNumber(collection[i].LineNumber));
            Assert.Equal(i, collection.IndexOf(collection[i]));
        }
    }

    [Fact]
    public void AppendRange_LargerThanCapacity_KeepsOnlyTheNewestLines_AndIndexesThem()
    {
        var collection = new DisplayLineCollection(capacity: 2);
        collection.AppendRange([Line(1)]);

        collection.AppendRange([Line(2), Line(3), Line(4)]);

        Assert.Equal([3L, 4L], collection.Select(l => l.LineNumber).ToArray());
        Assert.Null(collection.FindByLineNumber(1));
        Assert.Null(collection.FindByLineNumber(2));
        Assert.Equal(1, collection.IndexOf(collection.FindByLineNumber(4)!));
    }

    [Fact]
    public void DuplicateLineNumber_EvictingTheOlderCopy_KeepsTheNewerOneFindable()
    {
        // Reset markers all use line number 0.
        var collection = new DisplayLineCollection(capacity: 2);
        var older = Line(0);
        var newer = Line(0);
        collection.AppendRange([older, newer]);

        collection.AppendRange([Line(5)]);

        Assert.Same(newer, collection.FindByLineNumber(0));
        Assert.Equal(-1, collection.IndexOf(older));
    }

    [Fact]
    public void ImplementsReadOnlyNonGenericIList_SoWpfUsesAListCollectionView()
    {
        // A non-IList source gets WPF's EnumerableCollectionView, which re-copies every item on each Reset.
        var collection = new DisplayLineCollection(capacity: 10);
        collection.AppendRange([Line(1), Line(2)]);
        System.Collections.IList list = collection;

        Assert.True(list.IsReadOnly);
        Assert.Same(collection[1], list[1]);
        Assert.Equal(1, list.IndexOf(collection[1]));
        Assert.Throws<NotSupportedException>(() => list.Add(Line(3)));
        Assert.IsType<System.Windows.Data.ListCollectionView>(System.Windows.Data.CollectionViewSource.GetDefaultView(collection));
    }

    private static LogLineViewModel Line(long number) => new(number, $"line {number}", null, null, isBookmarked: false);
}
