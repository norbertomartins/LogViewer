using System.Drawing;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using LogViewer.UITests.TestUtilities;

namespace LogViewer.UITests;

/// <summary>
/// Verifies the Dark theme's colors are actually reaching rendered pixels, not just stored in
/// settings — the kind of regression where a control silently falls back to WPF Fluent's default
/// (light/accent-blue) styling instead of the app's dark palette, e.g. because it's missing the
/// <c>ItemContainerStyle</c> that <c>App.xaml</c>'s <c>LogListViewItemStyle</c> requires.
/// </summary>
public sealed class ThemeUITests : IDisposable
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);

    // Dark theme's nominal palette (src/LogViewer.Core/Theming/BuiltInThemes.cs).
    private static readonly Color DarkLogBackground = Color.FromArgb(0x1B, 0x1B, 0x1B);
    private static readonly Color DarkLogForeground = Color.FromArgb(0xD4, 0xD4, 0xD4);

    private readonly List<IDisposable> _disposables = [];
    private readonly List<string> _tempFiles = [];
    private UIA3Automation _automation = null!;
    private Application _app = null!;

    [Fact]
    public void DarkTheme_LogRowColors_MatchDarkPalette_NotLightTheme()
    {
        var lines = Enumerable.Range(1, 30).Select(i => $"2026-09-22 10:00:00 INFO plain line {i}");
        var path = CreateTempLogFile(string.Join('\n', lines) + "\n");

        var window = LaunchInDarkTheme(path);

        var list = UiHelpers.WaitFor(() => window.TryByAutomationId("LineListView"), "log list view");
        Assert.True(
            UiHelpers.WaitUntil(() => list.FindAllChildren(cf => cf.ByControlType(ControlType.ListItem)).Length > 0),
            "The document showed no lines after opening.");

        Thread.Sleep(500);

        if (PixelColorHelpers.ScreenCaptureIsUnavailable(window))
        {
            // No real desktop composition in this session (e.g. disconnected RDP) — nothing meaningful
            // to assert against a black capture. Same tolerance MainWindowUITests/BufferAndHighlightUITests use.
            return;
        }

        var row = list.FindAllChildren(cf => cf.ByControlType(ControlType.ListItem)).FirstOrDefault();
        if (row is null)
        {
            return;
        }

        using var image = Capture.Element(window);

        Assert.True(
            PixelColorHelpers.RegionContainsColor(image, window, row, c => PixelColorHelpers.IsColorNear(c, DarkLogBackground, 30)),
            "Expected a plain log row's background to be the dark theme's near-black background " +
            $"(~#{DarkLogBackground.R:X2}{DarkLogBackground.G:X2}{DarkLogBackground.B:X2}); it isn't — the dark palette may not be applied.");
        Assert.False(
            PixelColorHelpers.RegionContainsColor(image, window, row, c => c.R > 200 && c.G > 200 && c.B > 200),
            "Found a near-white pixel in a log row's background under the dark theme — light theme colors may be leaking through.");
    }

    [Fact]
    public void DarkTheme_SelectedRow_UsesDarkSelectionColors_NotDefaultAccentFill()
    {
        var lines = Enumerable.Range(1, 30).Select(i => $"2026-09-22 10:00:00 INFO plain line {i}");
        var path = CreateTempLogFile(string.Join('\n', lines) + "\n");

        var window = LaunchInDarkTheme(path);

        var list = UiHelpers.WaitFor(() => window.TryByAutomationId("LineListView"), "log list view");
        Assert.True(
            UiHelpers.WaitUntil(() => list.FindAllChildren(cf => cf.ByControlType(ControlType.ListItem)).Length > 0),
            "The document showed no lines after opening.");

        if (PixelColorHelpers.ScreenCaptureIsUnavailable(window))
        {
            return;
        }

        var rows = list.FindAllChildren(cf => cf.ByControlType(ControlType.ListItem));
        var targetRow = rows.Length > 1 ? rows[1] : rows.FirstOrDefault();
        if (targetRow is null)
        {
            return;
        }

        targetRow.AsListBoxItem().Select();
        Thread.Sleep(300);

        using var image = Capture.Element(window);

        // WPF Fluent's *default* ListViewItem selection template paints an accent-colored (blue-ish)
        // fill — exactly what App.xaml's LogListViewItemStyle exists to suppress by driving selection
        // off theme-aware brushes instead (see the comment at App.xaml around LogListViewItemStyle).
        Assert.False(
            PixelColorHelpers.RegionContainsColor(image, window, targetRow, IsAccentBlue),
            "Expected the selected row to use the app's dark selection styling, not WPF Fluent's default accent-blue fill — " +
            "this is the same class of bug as a list missing its ItemContainerStyle.");
        Assert.False(
            PixelColorHelpers.RegionContainsColor(image, window, targetRow, c => c.R > 220 && c.G > 220 && c.B > 220),
            "Expected the selected row under the dark theme to stay dark, not turn near-white.");
    }

    private static bool IsAccentBlue(Color c) => c.B > c.R + 30 && c.B > 150 && c.R < 150;

    private string CreateTempLogFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"logviewer-uitest-{Guid.NewGuid():N}.log");
        File.WriteAllText(path, content);
        _tempFiles.Add(path);
        return path;
    }

    private Window LaunchInDarkTheme(string logFilePath)
    {
        var fixture = new IsolatedSettingsFixture(IsolatedSettingsFixture.RestoringFileInDarkTheme(logFilePath));
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
