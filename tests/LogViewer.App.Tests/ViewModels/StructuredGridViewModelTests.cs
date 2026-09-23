using LogViewer.App.Tests.TestUtilities;
using LogViewer.App.ViewModels;
using LogViewer.Core.Structured;

namespace LogViewer.App.Tests.ViewModels;

public sealed class StructuredGridViewModelTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();

    // CLEF (Serilog compact JSON), auto-detected on open. Line 3 isn't JSON and has no event.
    private const string Log =
        "{\"@t\":\"2026-09-23T10:00:00Z\",\"@l\":\"Information\",\"@mt\":\"GET {RequestPath} in {Elapsed} ms\",\"RequestPath\":\"/orders\",\"Elapsed\":120,\"app\":\"shop\"}\n" +
        "{\"@t\":\"2026-09-23T10:00:01Z\",\"@l\":\"Error\",\"@mt\":\"GET {RequestPath} in {Elapsed} ms\",\"RequestPath\":\"/pay\",\"Elapsed\":9}\n" +
        "plain text line\n" +
        "{\"@t\":\"2026-09-23T10:00:02Z\",\"@l\":\"Information\",\"@mt\":\"GET {RequestPath} in {Elapsed} ms\",\"RequestPath\":\"/orders\",\"Elapsed\":30}\n";

    private (MainViewModel Main, TailDocumentViewModel Doc, StructuredGridViewModel Grid) Open()
    {
        var (main, _) = MainViewModelFactory.Create(new Core.Configuration.AppSettings { RestorePreviousSessionOnStartup = false });
        var doc = main.OpenPath(_tempDir.CreateFile("events.clef", Log));
        TestDispatcher.SpinUntil(() => doc.Lines.Count >= 4);
        var grid = new StructuredGridViewModel(doc);
        TestDispatcher.SpinUntil(() => grid.Rows.Count == 3);
        return (main, doc, grid);
    }

    [Fact]
    public void LoadsTheEvents_AndShowsTheMostCommonPropertiesAsColumns()
    {
        var (main, _, grid) = Open();

        Assert.Equal([1L, 2, 4], grid.Rows.Select(r => r.LineNumber));
        Assert.Equal(["Elapsed", "RequestPath", "app"], grid.PropertyColumns.Select(c => c.Name));
        Assert.Equal([3, 3, 1], grid.PropertyColumns.Select(c => c.Count));
        Assert.Equal(["Elapsed", "RequestPath", "app"], grid.VisiblePropertyColumns);
        Assert.Equal(["120", "/orders", "shop"], grid.Rows[0].Values);
        Assert.Equal("Showing 3 of 3 events", grid.StatusMessage);
        grid.Dispose();
        main.Dispose();
    }

    [Fact]
    public void SortBy_OrdersNumbersNumerically_AndFlipsOnTheSecondClick()
    {
        var (main, _, grid) = Open();

        grid.SortBy("Elapsed");
        Assert.Equal([2L, 4, 1], grid.Rows.Select(r => r.LineNumber));

        grid.SortBy("Elapsed");
        Assert.True(grid.SortDescending);
        Assert.Equal([1L, 4, 2], grid.Rows.Select(r => r.LineNumber));

        // Missing values stay last in either direction.
        grid.SortBy("app");
        Assert.Equal([1L, 2, 4], grid.Rows.Select(r => r.LineNumber));
        grid.SortBy("app");
        Assert.Equal([1L, 2, 4], grid.Rows.Select(r => r.LineNumber));
        grid.Dispose();
        main.Dispose();
    }

    [Fact]
    public void ValueFiltersAndSearch_NarrowTheRows()
    {
        var (main, _, grid) = Open();

        grid.AddFilter("RequestPath", "/orders", exclude: false);
        Assert.Equal([1L, 4], grid.Rows.Select(r => r.LineNumber));
        Assert.Equal("Showing 2 of 3 events", grid.StatusMessage);

        grid.AddFilter(StructuredGridViewModel.LevelColumn, "Information", exclude: true);
        Assert.Empty(grid.Rows);

        grid.RemoveFilterCommand.Execute(grid.Filters[1]);
        grid.SearchText = "shop";
        Assert.Equal([1L], grid.Rows.Select(r => r.LineNumber));

        // Line numbers and times are unique per row: not filterable.
        grid.AddFilter(StructuredGridViewModel.LineColumn, "1", exclude: false);
        Assert.Single(grid.Filters);

        grid.ClearFiltersCommand.Execute(null);
        Assert.Equal(3, grid.Rows.Count);
        Assert.Null(grid.SearchText);
        grid.Dispose();
        main.Dispose();
    }

    [Fact]
    public void UntickingAColumn_RemovesItFromTheRows_AndKeepsTheChoiceOnRefresh()
    {
        var (main, _, grid) = Open();
        var columnsChanged = 0;
        grid.ColumnsChanged += () => columnsChanged++;

        grid.PropertyColumns.Single(c => c.Name == "Elapsed").IsVisible = false;

        Assert.Equal(1, columnsChanged);
        Assert.Equal(["RequestPath", "app"], grid.VisiblePropertyColumns);
        Assert.Equal(["/orders", "shop"], grid.Rows[0].Values);

        grid.RefreshCommand.Execute(null);
        TestDispatcher.SpinUntil(() => !grid.IsLoading);
        Assert.Equal(["RequestPath", "app"], grid.VisiblePropertyColumns);
        grid.Dispose();
        main.Dispose();
    }

    [Fact]
    public void JumpToLine_SelectsTheLineInTheDocument()
    {
        var (main, doc, grid) = Open();

        grid.JumpToLineCommand.Execute(grid.Rows.Single(r => r.LineNumber == 4));

        Assert.Equal(4, doc.SelectedLine?.LineNumber);
        Assert.Equal("/pay", StructuredGridViewModel.CellValue(grid.Rows[1], "RequestPath"));
        Assert.Equal("Error", StructuredGridViewModel.CellValue(grid.Rows[1], StructuredFieldResolver.LevelField));
        grid.Dispose();
        main.Dispose();
    }

    public void Dispose() => _tempDir.Dispose();
}
