using LogViewer.Core.Bookmarks;

namespace LogViewer.Core.Tests.Bookmarks;

public sealed class BookmarkManagerTests
{
    [Fact]
    public void Toggle_OnUnbookmarkedLine_AddsBookmark()
    {
        var manager = new BookmarkManager();

        manager.Toggle(10);

        Assert.True(manager.IsBookmarked(10));
        Assert.Single(manager.Bookmarks);
    }

    [Fact]
    public void Toggle_OnBookmarkedLine_RemovesBookmark()
    {
        var manager = new BookmarkManager();
        manager.Toggle(10);

        manager.Toggle(10);

        Assert.False(manager.IsBookmarked(10));
        Assert.Empty(manager.Bookmarks);
    }

    [Fact]
    public void Next_ReturnsNearestBookmarkStrictlyAfter()
    {
        var manager = new BookmarkManager();
        manager.Toggle(5);
        manager.Toggle(20);
        manager.Toggle(50);

        Assert.Equal(20, manager.Next(5));
        Assert.Equal(50, manager.Next(20));
        Assert.Null(manager.Next(50));
    }

    [Fact]
    public void Previous_ReturnsNearestBookmarkStrictlyBefore()
    {
        var manager = new BookmarkManager();
        manager.Toggle(5);
        manager.Toggle(20);
        manager.Toggle(50);

        Assert.Equal(20, manager.Previous(50));
        Assert.Equal(5, manager.Previous(20));
        Assert.Null(manager.Previous(5));
    }

    [Fact]
    public void Clear_RemovesAllBookmarks()
    {
        var manager = new BookmarkManager();
        manager.Toggle(1);
        manager.Toggle(2);

        manager.Clear();

        Assert.Empty(manager.Bookmarks);
        Assert.Null(manager.Next(0));
    }
}
