using LogViewer.App.Models;
using LogViewer.App.Tests.TestUtilities;
using LogViewer.App.ViewModels;

namespace LogViewer.App.Tests.ViewModels;

public sealed class MultiLineEntryTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();

    private const string Log =
        "2026-09-23 10:00:00 INFO started\n" +
        "2026-09-23 10:00:01 ERROR request failed\n" +
        "System.InvalidOperationException: boom\n" +
        "   at Shop.Orders.Load()\n" +
        "   at Shop.Api.Get()\n" +
        "2026-09-23 10:00:02 INFO recovered\n";

    private OpenDocument Open()
    {
        var settings = new Core.Configuration.AppSettings { RestorePreviousSessionOnStartup = false };
        var (main, _) = MainViewModelFactory.Create(settings);
        var doc = main.OpenPath(_tempDir.CreateFile("trace.log", Log));
        TestDispatcher.SpinUntil(() => doc.Lines.Count >= 6);
        return new OpenDocument(main, doc);
    }

    [Fact]
    public void AStackTraceAndItsHeaderLineJoinTheLogLineAbove()
    {
        using var t = Open();
        var head = t.Line(2);

        Assert.Equal(3, head.GroupedLineCount);
        Assert.True(head.IsGroupHead);
        Assert.All([t.Line(3), t.Line(4), t.Line(5)], l => Assert.Same(head, l.GroupHead));
        Assert.Null(t.Line(1).GroupHead);
        Assert.Null(t.Line(6).GroupHead);
        Assert.False(t.Line(1).IsGroupHead);
        Assert.Equal("▾", head.GroupToggleText);
    }

    [Fact]
    public void ToggleEntry_HidesAndShowsItsContinuationLines()
    {
        using var t = Open();
        var head = t.Line(2);
        var filterChanges = 0;
        t.Doc.FilterChanged += () => filterChanges++;

        // Toggling from a continuation line collapses its entry.
        t.Doc.ToggleEntryCollapsedCommand.Execute(t.Line(4));

        Assert.True(head.IsGroupCollapsed);
        Assert.Equal("▸ +3", head.GroupToggleText);
        Assert.True(t.Doc.HasCollapsedEntries);
        Assert.True(t.Doc.IsHiddenByCollapse(t.Line(3)));
        Assert.False(t.Doc.IsHiddenByCollapse(head));
        Assert.False(t.Doc.IsHiddenByCollapse(t.Line(6)));
        Assert.Equal(1, filterChanges);

        t.Doc.ToggleEntryCollapsedCommand.Execute(head);

        Assert.False(head.IsGroupCollapsed);
        Assert.False(t.Doc.HasCollapsedEntries);
        Assert.False(t.Doc.IsHiddenByCollapse(t.Line(3)));
    }

    [Fact]
    public void CollapseAll_AppliesToEveryEntry_AndExportKeepsTheHiddenLines()
    {
        using var t = Open();
        t.Doc.CollapseMultiLineEntries = true;

        Assert.True(t.Line(2).IsGroupCollapsed);
        Assert.True(t.Doc.IsHiddenByCollapse(t.Line(5)));

        var exported = t.Doc.WithCollapsedContinuations([t.Line(2), t.Line(6)]);
        Assert.Equal([2L, 3, 4, 5, 6], exported.Select(l => l.LineNumber));

        t.Doc.CollapseMultiLineEntries = false;
        Assert.False(t.Line(2).IsGroupCollapsed);
    }

    [Fact]
    public void NavigatingToAHiddenLine_ExpandsItsEntry()
    {
        using var t = Open();
        t.Doc.CollapseMultiLineEntries = true;
        LogLineViewModel? scrolledTo = null;
        t.Doc.ScrollToLineRequested += l => scrolledTo = l;

        Assert.True(t.Doc.TryNavigateToLineNumber(4));

        Assert.False(t.Line(2).IsGroupCollapsed);
        Assert.Same(t.Line(4), scrolledTo);
        Assert.Same(t.Line(4), t.Doc.SelectedLine);
    }

    public void Dispose() => _tempDir.Dispose();

    private sealed class OpenDocument(MainViewModel main, TailDocumentViewModel doc) : IDisposable
    {
        public TailDocumentViewModel Doc { get; } = doc;

        public LogLineViewModel Line(long number) => Doc.Lines.Single(l => l.LineNumber == number);

        public void Dispose() => main.Dispose();
    }
}
