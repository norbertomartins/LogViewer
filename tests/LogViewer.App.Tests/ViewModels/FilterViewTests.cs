using LogViewer.App.Services;
using LogViewer.App.Tests.TestUtilities;
using LogViewer.Core.Analysis;
using LogViewer.Core.Configuration;
using NSubstitute;

namespace LogViewer.App.Tests.ViewModels;

public sealed class FilterViewTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();

    private static string Log(string service) => string.Concat(Enumerable.Range(0, 60).Select(i =>
        $"2026-09-23 10:{i:00}:00.000 [{(i % 10 == 0 ? "ERROR" : "INFO")}] {service} request_id=req-{i % 3} step {i}\n"));

    [Fact]
    public void SavedView_AppliesTheSameFiltersToAnotherDocument()
    {
        var dialogs = Substitute.For<IDialogService>();
        dialogs.ShowTextPrompt(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string?>()).Returns("req-1 errors, last 30m");
        var (main, settings) = MainViewModelFactory.Create(dialogService: dialogs);
        var a = main.OpenPath(_tempDir.CreateFile("a.log", Log("orders")));
        var b = main.OpenPath(_tempDir.CreateFile("b.log", Log("payments")));
        TestDispatcher.SpinUntil(() => a.Lines.Count >= 60 && b.Lines.Count >= 60);

        a.MinLevel = "Error";
        a.FilterByCorrelationCommand.Execute(new CorrelationId("request_id", "req-1"));
        a.TimeFilterFromText = "-30m";
        a.ApplyTimeFilterCommand.Execute(null);
        a.SaveFilterViewCommand.Execute(null);

        var view = Assert.Single(settings.FilterViews);
        Assert.Equal("req-1 errors, last 30m", view.Name);
        Assert.Equal("-30m", view.TimeFilterFromText);
        Assert.Same(view, Assert.Single(b.FilterViews));

        b.ApplyFilterViewCommand.Execute(view);

        Assert.Equal("Error", b.MinLevel);
        Assert.Equal("req-1", b.CorrelationFilter?.Value);
        Assert.True(b.IsTimeFilterActive);
        Assert.Equal(new DateTimeOffset(2026, 9, 23, 10, 29, 0, TimeSpan.Zero), b.TimeFilterFrom); // newest line 10:59 - 30m

        b.DeleteFilterViewCommand.Execute(view);
        Assert.Empty(settings.FilterViews);
        Assert.Empty(a.FilterViews);
        main.Dispose();
    }

    [Fact]
    public void SaveWithNoActiveFilter_DoesNotPromptOrSave()
    {
        var dialogs = Substitute.For<IDialogService>();
        var (main, settings) = MainViewModelFactory.Create(dialogService: dialogs);
        var doc = main.OpenPath(_tempDir.CreateFile("c.log", Log("x")));

        doc.SaveFilterViewCommand.Execute(null);

        dialogs.DidNotReceiveWithAnyArgs().ShowTextPrompt(default!, default!, default);
        Assert.Empty(settings.FilterViews);
        main.Dispose();
    }

    [Fact]
    public void TimeAndCorrelationFilters_SurviveSessionRestore()
    {
        var path = _tempDir.CreateFile("r.log", Log("restore"));
        var settings = new AppSettings { RestorePreviousSessionOnStartup = false };
        var (main, _) = MainViewModelFactory.Create(settings);
        var doc = main.OpenPath(path);
        TestDispatcher.SpinUntil(() => doc.Lines.Count >= 60);
        doc.FilterByCorrelationCommand.Execute(new CorrelationId("request_id", "req-2"));
        doc.TimeFilterFromText = "10:20";
        doc.TimeFilterToText = "10:40";
        doc.ApplyTimeFilterCommand.Execute(null);
        main.SaveAndDispose();

        var entry = Assert.Single(settings.RecentSources);
        Assert.Equal("req-2", entry.CorrelationFilterValue);
        Assert.Equal("10:20", entry.TimeFilterFromText);

        settings.RestorePreviousSessionOnStartup = true;
        var (restoredMain, _) = MainViewModelFactory.Create(settings);
        var restored = Assert.Single(restoredMain.Documents);
        TestDispatcher.SpinUntil(() => restored.Lines.Count >= 60 && restored.IsTimeFilterActive);

        Assert.Equal("req-2", restored.CorrelationFilter?.Value);
        Assert.Equal(new DateTimeOffset(2026, 9, 23, 10, 20, 0, TimeSpan.Zero), restored.TimeFilterFrom);
        Assert.Equal(new DateTimeOffset(2026, 9, 23, 10, 40, 0, TimeSpan.Zero), restored.TimeFilterTo);
        restoredMain.Dispose();
    }

    public void Dispose() => _tempDir.Dispose();
}
