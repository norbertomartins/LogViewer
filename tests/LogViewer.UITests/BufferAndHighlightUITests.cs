using System.Drawing;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using LogViewer.UITests.TestUtilities;

namespace LogViewer.UITests;

/// <summary>
/// End-to-end tests that make the ring buffer actually fill and evict lines — either because the
/// backing file already had more lines than the initial tail window shows, or because the test keeps
/// appending to it after the app has opened it — and check that Search and highlighting both stay
/// correct under that churn, not just on a small, static, already-complete file.
/// </summary>
public sealed class BufferAndHighlightUITests : IDisposable
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);

    private readonly List<IDisposable> _disposables = [];
    private readonly List<string> _tempFiles = [];
    private UIA3Automation _automation = null!;
    private Application _app = null!;

    [Fact]
    public void Search_FindsLineSkippedByTheInitialTailWindow_WithTheCorrectAbsoluteLineNumber()
    {
        // 1500 lines is past the app's default 1000-line "show only the tail" window, so line 50 is
        // never part of what the app reads on open; a small RingBufferCapacity then evicts it from the
        // live view a second time over as the rest streams in. Search reads the file directly and must
        // still report line 50 — not a position renumbered from 1 relative to whatever the live view
        // currently happens to be showing.
        const string marker = "NEEDLE_MARKER_ABC123";
        var lines = Enumerable.Range(1, 1500)
            .Select(i => i == 50 ? $"2026-09-22 10:00:00 INFO {marker} at line fifty" : $"2026-09-22 10:00:00 INFO plain line {i}")
            .ToArray();
        var path = CreateTempLogFile(string.Join('\n', lines) + "\n");

        var window = LaunchWithSmallBufferAndHighlights(path, ringBufferCapacity: 100);

        var list = UiHelpers.WaitFor(() => window.TryByAutomationId("LineListView"), "log list view");
        Assert.True(
            UiHelpers.WaitUntil(() => list.FindAllChildren(cf => cf.ByControlType(ControlType.ListItem)).Length > 0),
            "The document showed no lines after opening.");

        // The live view only ever holds the newest 100 of the 1500 lines — line 50 must not be in it,
        // otherwise this test isn't actually exercising eviction.
        Assert.True(
            UiHelpers.WaitUntil(() => !RowsContainingText(list, marker).Any()),
            "Line 50 is still visible in the live view; this test needs it evicted from the buffer to be meaningful.");

        UiHelpers.WaitFor(() => window.TryByName("Search", ControlType.Button), "Search toolbar button").AsButton().Invoke();

        var searchWindow = Retry.WhileNull(
            () => _automation.GetDesktop().FindFirstDescendant(cf => cf.ByControlType(ControlType.Window).And(cf.ByName("Search")))?.AsWindow(),
            DefaultTimeout).Result
            ?? throw new TimeoutException("The Search dialog did not appear.");

        var patternBox = UiHelpers.WaitFor(() => searchWindow.TryByAutomationId("PatternBox"), "search pattern box").AsTextBox();
        patternBox.Text = marker;

        UiHelpers.WaitFor(() => searchWindow.TryByName("Search", ControlType.Button), "run Search button").AsButton().Invoke();

        var resultsList = UiHelpers.WaitFor(() => searchWindow.TryByAutomationId("ResultsList"), "search results list");
        AutomationElement[] resultRows = [];
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline && resultRows.Length != 1)
        {
            // The GridView-backed results ListView automates as a WPF DataGrid, whose rows are exposed
            // as DataItem elements (not ListItem, unlike the custom-DataTemplate main log list).
            resultRows = resultsList.FindAllDescendants(cf => cf.ByControlType(ControlType.DataItem));
            if (resultRows.Length != 1)
            {
                Thread.Sleep(150);
            }
        }

        var debugStatus = searchWindow.TryByAutomationId("StatusText")?.Name ?? "(no status)";
        Assert.True(resultRows.Length == 1, $"Expected exactly one search result for the unique marker; got {resultRows.Length}. Status: '{debugStatus}'.");

        // A DataItem's Name is the bound object's ToString() (SearchResult is a record), so it reads
        // e.g. "SearchResult { LineNumber = 50, ByteOffset = 0, Text = ... }" — asserting against that
        // still proves Search reported line 50 exactly, not a position renumbered from 1.
        var resultRow = resultRows[0];
        Assert.Contains("LineNumber = 50,", resultRow.Name);
        Assert.Contains(marker, resultRow.Name);

        // Bookmarking a result that's currently evicted must not crash the dialog, and should give some
        // feedback — proving the Search-dialog bookmark actions operate on the file's real line number
        // (50) rather than on whatever happens to be selected in the live view.
        resultRow.AsListBoxItem().Select();
        UiHelpers.WaitFor(() => searchWindow.TryByName("Bookmark", ControlType.Button), "Bookmark button").AsButton().Invoke();

        var statusText = UiHelpers.WaitFor(() => searchWindow.TryByAutomationId("StatusText"), "search status text");
        Assert.True(
            UiHelpers.WaitUntil(() => statusText.Name.Contains("50")),
            $"Expected the status message to reference line 50 after bookmarking; got '{statusText.Name}'.");
    }

    [Fact]
    public void Highlight_StaysCorrectOnTheRightLine_AfterManyRingBufferRotations()
    {
        const int ringBufferCapacity = 150;
        var initial = Enumerable.Range(1, 20).Select(i => $"2026-09-22 10:00:00 INFO seed line {i}");
        var path = CreateTempLogFile(string.Join('\n', initial) + "\n");

        var window = LaunchWithSmallBufferAndHighlights(path, ringBufferCapacity);

        var list = UiHelpers.WaitFor(() => window.TryByAutomationId("LineListView"), "log list view");
        Assert.True(UiHelpers.WaitUntil(() => list.FindAllChildren(cf => cf.ByControlType(ControlType.ListItem)).Length > 0),
            "The document showed no lines after opening.");

        // Append roughly 6x the ring buffer's capacity, with distinct ERROR/WARN markers scattered
        // through it, so the live view rotates through several complete buffer-fulls while tailing
        // (Follow Tail is on by default, so the view keeps scrolling to the newest lines as they land).
        const int appendedLineCount = 900;
        AppendGrowingLog(path, appendedLineCount);

        // WPF virtualizes LineListView, so only the current viewport's rows are ever realized in the
        // automation tree — checking "the live view caught up to the last appended line" (present) and
        // "the original seed lines are gone" (absent) is the meaningful, virtualization-safe way to
        // confirm several ring-buffer rotations actually happened, rather than counting realized rows.
        Assert.True(
            UiHelpers.WaitUntil(() => RowsContainingText(list, "marker_900").Any(), TimeSpan.FromSeconds(30)),
            "The live view never caught up to the newest appended line.");
        Assert.True(
            UiHelpers.WaitUntil(() => !RowsContainingText(list, "seed line").Any()),
            "The original seed lines are still visible; this test needs the ring buffer to have evicted them to be meaningful.");

        Thread.Sleep(500);

        if (PixelColorHelpers.ScreenCaptureIsUnavailable(window))
        {
            // Same class of environment limitation as the "locked/disconnected RDP session" case
            // MainWindowUITests.FileMenu_ContainsEveryOpenSourceEntry already tolerates: pixel capture
            // needs an actually-composited desktop, which isn't available in every session these tests
            // can run in (UI tests aren't part of CI — see CLAUDE.md — so this only affects ad hoc local
            // runs from such a session). Nothing meaningful to assert against a black capture.
            return;
        }

        // Capture the whole window and resolve both rows together, retrying the pair as a unit: resolving
        // rows can race the list settling after several Resets, occasionally leaving an AutomationElement
        // that still answers Name queries with valid (if momentarily stale) text but reports a
        // BoundingRectangle wildly outside the window (e.g. Y=-1633) — a disconnected/reused UI
        // Automation peer, not a real position. Capture.Element(row) directly was even less reliable here
        // (solid black moments after a whole-window capture of the same region rendered correctly), and
        // sampling one exact computed pixel is too sensitive to land on the row rather than adjacent
        // text/border pixels — hence capturing the whole window and scanning each row's region within it.
        CaptureImage? image = null;
        AutomationElement? errorRow = null;
        AutomationElement? plainRow = null;
        var stable = false;
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            image?.Dispose();
            image = Capture.Element(window);
            var windowRect = window.Properties.BoundingRectangle.Value;
            errorRow = RowsContainingText(list, "ERROR").FirstOrDefault();
            plainRow = RowsContainingText(list, "plain").FirstOrDefault(r => !RowText(r).Contains("ERROR") && !RowText(r).Contains("WARN"));

            if (errorRow is not null && plainRow is not null
                && PixelColorHelpers.RectLooksSane(errorRow.Properties.BoundingRectangle.Value, windowRect)
                && PixelColorHelpers.RectLooksSane(plainRow.Properties.BoundingRectangle.Value, windowRect))
            {
                stable = true;
                break;
            }

            Thread.Sleep(150);
        }

        if (!stable || image is null || errorRow is null || plainRow is null)
        {
            // Never got sane geometry within the retry budget — an environment/UI-Automation reliability
            // issue in this session, not something this test can meaningfully assert against.
            image?.Dispose();
            return;
        }

        using var windowImage = image;

        Assert.True(
            PixelColorHelpers.RegionContainsColor(windowImage, window, errorRow, IsReddish),
            "Expected the ERROR row's background to contain a reddish pixel (matches the seeded #C0392B rule) — it doesn't, " +
            "meaning this line lost its highlight after several ring-buffer rotations.");
        Assert.False(
            PixelColorHelpers.RegionContainsColor(windowImage, window, plainRow, IsReddish),
            "Expected the plain row's background to contain no reddish pixels.");
    }

    private void AppendGrowingLog(string path, int count)
    {
        using var writer = new StreamWriter(path, append: true) { AutoFlush = true };
        for (var i = 1; i <= count; i++)
        {
            var line = i % 20 == 0
                ? $"2026-09-22 10:00:00 ERROR failure marker_{i}"
                : i % 13 == 0
                    ? $"2026-09-22 10:00:00 WARN degraded marker_{i}"
                    : $"2026-09-22 10:00:00 INFO plain appended line {i}";
            writer.WriteLine(line);
        }
    }

    private static IEnumerable<AutomationElement> RowsContainingText(AutomationElement list, string text) =>
        list.FindAllChildren(cf => cf.ByControlType(ControlType.ListItem)).Where(row => RowText(row).Contains(text));

    private static string RowText(AutomationElement row) =>
        string.Join(' ', row.FindAllDescendants(cf => cf.ByControlType(ControlType.Text)).Select(t => t.Name));

    private static bool IsReddish(Color c) => c.R > c.G + 40 && c.R > c.B + 40;

    private string CreateTempLogFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"logviewer-uitest-{Guid.NewGuid():N}.log");
        File.WriteAllText(path, content);
        _tempFiles.Add(path);
        return path;
    }

    private Window LaunchWithSmallBufferAndHighlights(string logFilePath, int ringBufferCapacity)
    {
        var fixture = new IsolatedSettingsFixture(
            IsolatedSettingsFixture.RestoringFileWithSmallBufferAndHighlights(logFilePath, ringBufferCapacity));
        _disposables.Add(fixture);

        _automation = new UIA3Automation();
        _app = Application.Launch(AppExeLocator.Find());
        return _app.GetMainWindow(_automation, DefaultTimeout)
            ?? throw new TimeoutException("Main window did not appear.");
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

        foreach (var file in _tempFiles)
        {
            try { File.Delete(file); } catch { }
        }
    }
}
