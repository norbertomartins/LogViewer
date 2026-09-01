using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using LogViewer.UITests.TestUtilities;

namespace LogViewer.UITests;

/// <summary>
/// End-to-end UI tests that need a document already open. Each test seeds an isolated
/// <c>settings.json</c> that restores one of the <c>samples/</c> log files on startup, launches the
/// real <c>LogViewer.App.exe</c>, and drives it via UIA patterns (no synthetic mouse/keyboard).
/// </summary>
public sealed class DocumentUITests : IDisposable
{
    private readonly List<IDisposable> _disposables = [];
    private UIA3Automation _automation = null!;
    private Application _app = null!;

    private Window LaunchRestoring(string sampleFile)
    {
        var path = AppExeLocator.Sample("timeline", sampleFile);
        Assert.True(File.Exists(path), $"Sample log not found: {path}");

        var fixture = new IsolatedSettingsFixture(IsolatedSettingsFixture.RestoringFile(path));
        _disposables.Add(fixture);

        _automation = new UIA3Automation();
        _app = Application.Launch(AppExeLocator.Find());
        return _app.GetMainWindow(_automation, UiHelpers.Timeout)
            ?? throw new TimeoutException("Main window did not appear.");
    }

    [Fact]
    public void RestoredSession_OpensSampleLog_AndShowsLines()
    {
        var window = LaunchRestoring("payments-service.log");

        var list = UiHelpers.WaitFor(() => window.TryByAutomationId("LineListView"), "log list view");

        var hasRows = UiHelpers.WaitUntil(() =>
            list.FindAllChildren(cf => cf.ByControlType(ControlType.ListItem)).Length > 0);
        Assert.True(hasRows, "The restored document showed no log lines.");
    }

    [Fact]
    public void DocumentToolbar_ExposesIconButtonsByAccessibleName()
    {
        var window = LaunchRestoring("payments-service.log");

        foreach (var name in new[] { "Follow Tail", "Structured View", "Timeline", "Search", "Export" })
        {
            Assert.NotNull(window.TryByName(name, ControlType.Button) ?? window.TryByName(name, ControlType.CheckBox));
        }
    }

    [Fact]
    public void Timeline_Toggle_ShowsAndHidesTheHistogramStrip()
    {
        var window = LaunchRestoring("orders-service.clef");

        Assert.Null(window.TryByAutomationId("TimelineStrip"));

        var timeline = UiHelpers.WaitFor(
            () => window.TryByName("Timeline", ControlType.Button) ?? window.TryByName("Timeline", ControlType.CheckBox),
            "Timeline toggle").AsToggleButton();
        timeline.Toggle();

        Assert.True(UiHelpers.WaitUntil(() => window.TryByAutomationId("TimelineStrip") is not null),
            "The timeline strip did not appear after toggling it on.");

        // The .clef sample has hundreds of timestamped events, so bins (rendered as buttons) should appear.
        Assert.True(UiHelpers.WaitUntil(() =>
                window.TryByAutomationId("TimelineStrip")?.FindAllDescendants(cf => cf.ByControlType(ControlType.Button)).Length > 0),
            "The timeline produced no volume bars.");

        timeline.Toggle();
        Assert.True(UiHelpers.WaitUntil(() => window.TryByAutomationId("TimelineStrip") is null),
            "The timeline strip did not disappear after toggling it off.");
    }

    [Fact]
    public void TextFilter_HidesNonMatchingLines()
    {
        var window = LaunchRestoring("payments-service.log");

        var list = UiHelpers.WaitFor(() => window.TryByAutomationId("LineListView"), "log list view");
        Assert.True(UiHelpers.WaitUntil(() => list.FindAllChildren(cf => cf.ByControlType(ControlType.ListItem)).Length > 5));
        var before = list.FindAllChildren(cf => cf.ByControlType(ControlType.ListItem)).Length;

        var filter = UiHelpers.WaitFor(() => window.TryByName("Text filter", ControlType.Edit), "text filter box").AsTextBox();
        filter.Text = "ERROR";

        Assert.True(
            UiHelpers.WaitUntil(() => list.FindAllChildren(cf => cf.ByControlType(ControlType.ListItem)).Length < before),
            "Applying a text filter did not reduce the visible line count.");
    }

    public void Dispose()
    {
        try { _app?.Close(); } catch { }
        try { if (_app is { HasExited: false }) _app.Kill(); } catch { }
        try { _app?.Dispose(); } catch { }
        _automation?.Dispose();
        foreach (var d in _disposables)
        {
            try { d.Dispose(); } catch { }
        }
    }
}
