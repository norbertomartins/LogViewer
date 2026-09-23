using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using LogViewer.UITests.TestUtilities;

namespace LogViewer.UITests;

/// <summary>
/// End-to-end checks for the Phase 9 document features: the Δ/time-navigation/whole-file/exceptions toolbar
/// buttons exist, and the whole-file browser, exceptions panel and custom-format editor windows actually open
/// and render (XAML/binding errors only surface at runtime).
/// </summary>
public sealed class Phase9UITests : IDisposable
{
    private readonly List<IDisposable> _disposables = [];
    private UIA3Automation _automation = null!;
    private Application _app = null!;

    private Window LaunchRestoring(string sampleFile, string sampleFolder = "timeline")
    {
        var path = AppExeLocator.Sample(sampleFolder, sampleFile);
        var fixture = new IsolatedSettingsFixture(IsolatedSettingsFixture.RestoringFile(path));
        _disposables.Add(fixture);

        _automation = new UIA3Automation();
        _app = Application.Launch(AppExeLocator.Find());
        var window = _app.GetMainWindow(_automation, UiHelpers.Timeout)
            ?? throw new TimeoutException("Main window did not appear.");
        UiHelpers.MaximizeWindow(window);
        UiHelpers.WaitFor(() => window.TryByAutomationId("LineListView"), "log list view");
        return window;
    }

    // Owned WPF windows appear under their owner in the UIA tree, not as desktop children — search descendants.
    private Window WaitForTopLevelWindow(string titlePrefix) =>
        UiHelpers.WaitFor(
            () => _automation.GetDesktop()
                .FindAllDescendants(cf => cf.ByControlType(ControlType.Window))
                .FirstOrDefault(w => w.Name?.StartsWith(titlePrefix, StringComparison.Ordinal) == true),
            $"window '{titlePrefix}…'").AsWindow();

    private static AutomationElement ToolbarButton(Window window, string name) =>
        UiHelpers.WaitFor(() => window.TryByName(name, ControlType.Button) ?? window.TryByName(name, ControlType.CheckBox), name);

    [Fact]
    public void DocumentToolbar_ExposesThePhase8Buttons()
    {
        var window = LaunchRestoring("payments-service.log");

        foreach (var name in new[] { "Time delta column", "Time navigation", "Browse whole file", "Exceptions" })
        {
            Assert.NotNull(ToolbarButton(window, name));
        }

        ToolbarButton(window, "Time delta column").AsToggleButton().Toggle();
        Assert.Equal(ToggleState.On, ToolbarButton(window, "Time delta column").AsToggleButton().ToggleState);
    }

    [Fact]
    public void BrowseWholeFile_OpensTheVirtualizedBrowserWithLines()
    {
        var window = LaunchRestoring("payments-service.log");

        ToolbarButton(window, "Browse whole file").AsButton().Invoke();

        var browser = WaitForTopLevelWindow("Whole file");
        var list = UiHelpers.WaitFor(() => browser.TryByAutomationId("LineListView"), "browser line list");
        Assert.True(UiHelpers.WaitUntil(() => list.FindAllChildren(cf => cf.ByControlType(ControlType.ListItem)).Length > 0),
            "The whole-file browser showed no lines.");
    }

    [Fact]
    public void Exceptions_OpensTheGroupsPanel()
    {
        var window = LaunchRestoring("payments-service.log");

        ToolbarButton(window, "Exceptions").AsButton().Invoke();

        var panel = WaitForTopLevelWindow("Exceptions");
        Assert.NotNull(UiHelpers.WaitFor(() => panel.TryByAutomationId("ExceptionGroupsList"), "exception groups list"));
    }

    [Fact]
    public void ToolsMenu_OpensTheCustomFormatsEditor()
    {
        var window = LaunchRestoring("payments-service.log");

        UiHelpers.InvokeMenuPath(window, "Tools", "Custom Log Formats...");

        var editor = WaitForTopLevelWindow("Custom Log Formats");
        Assert.NotNull(UiHelpers.WaitFor(() => editor.TryByAutomationId("CustomFormatsList"), "custom formats list"));
        editor.Close();
    }

    [Fact]
    public void ScrollMarkerStrip_RendersErrorMarks()
    {
        var window = LaunchRestoring("payments-service.log"); // contains [ERROR] lines

        var strip = UiHelpers.WaitFor(() => window.TryByAutomationId("ScrollMarkerStrip"), "scroll marker strip");
        if (PixelColorHelpers.ScreenCaptureIsUnavailable(window))
        {
            return; // disconnected RDP session: nothing is composited, so pixels can't be asserted
        }

        Assert.True(UiHelpers.WaitUntil(() => StripHasColor(window, strip, c => c.R > 200 && c.G < 90 && c.B < 90)),
            "No red (error) mark was drawn on the scroll-marker strip.");
    }

    private static bool StripHasColor(Window window, AutomationElement strip, Func<System.Drawing.Color, bool> predicate)
    {
        using var image = FlaUI.Core.Capturing.Capture.Element(window);
        var bitmap = image.Bitmap;
        var windowRect = window.Properties.BoundingRectangle.Value;
        var rect = strip.Properties.BoundingRectangle.Value;
        var scaleX = bitmap.Width / windowRect.Width;
        var scaleY = bitmap.Height / windowRect.Height;
        var x = Math.Clamp((int)((rect.Left + (rect.Width / 2.0) - windowRect.Left) * scaleX), 0, bitmap.Width - 1);
        var top = Math.Clamp((int)((rect.Top - windowRect.Top) * scaleY), 0, bitmap.Height - 1);
        var bottom = Math.Clamp((int)((rect.Bottom - windowRect.Top) * scaleY), 0, bitmap.Height - 1);
        for (var y = top; y <= bottom; y++)
        {
            if (predicate(bitmap.GetPixel(x, y)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SelectedRowContains(AutomationElement list, string text) =>
        list.AsListBox().SelectedItem?.FindAllDescendants(cf => cf.ByControlType(ControlType.Text))
            .Any(t => t.Name?.Contains(text, StringComparison.Ordinal) == true) == true;

    [Fact]
    public void TimePopup_GoToTime_SelectsTheFirstLineAtThatTime()
    {
        var window = LaunchRestoring("checkout-service.log", "correlation");
        var list = UiHelpers.WaitFor(() => window.TryByAutomationId("LineListView"), "log list view");
        Assert.True(UiHelpers.WaitUntil(() => list.FindAllChildren(cf => cf.ByControlType(ControlType.ListItem)).Length > 0));

        ToolbarButton(window, "Time navigation").AsToggleButton().Toggle();

        // The popup is its own top-level HWND, so look for its controls from the desktop.
        var desktop = _automation.GetDesktop();
        var box = UiHelpers.WaitFor(() => desktop.FindFirstDescendant(cf => cf.ByName("Time to go to").And(cf.ByControlType(ControlType.Edit))), "go-to-time box");
        box.Patterns.Value.Pattern.SetValue("10:01:30");
        UiHelpers.WaitFor(() => desktop.FindFirstDescendant(cf => cf.ByName("Go").And(cf.ByControlType(ControlType.Button))), "Go button").AsButton().Invoke();

        Assert.True(UiHelpers.WaitUntil(() => SelectedRowContains(list, "10:01:30")), "Go to time did not select the 10:01:30 line.");
    }

    [Fact]
    public void CollapseStackTraces_HidesTheFrames_AndTheRowButtonExpandsOneEntry()
    {
        var window = LaunchRestoring("checkout-service.log", "correlation");
        var list = UiHelpers.WaitFor(() => window.TryByAutomationId("LineListView"), "log list view");
        bool FramesShown() => list.FindAllDescendants(cf => cf.ByControlType(ControlType.Text))
            .Any(t => t.Name?.TrimStart().StartsWith("at Shop.", StringComparison.Ordinal) == true);

        Assert.True(UiHelpers.WaitUntil(FramesShown), "No stack frame was shown before collapsing.");

        ToolbarButton(window, "Collapse stack traces").AsToggleButton().Toggle();

        Assert.True(UiHelpers.WaitUntil(() => !FramesShown()), "Stack frames were still shown after collapsing.");
        var expand = UiHelpers.WaitFor(
            () => list.FindFirstDescendant(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Expand or collapse entry"))),
            "entry expand button").AsButton();
        expand.Invoke();

        Assert.True(UiHelpers.WaitUntil(FramesShown), "Expanding the entry did not show its stack frames.");
    }

    [Fact]
    public void ColumnView_ShowsTheEventsWithPropertyColumns_AndSearchNarrowsThem()
    {
        var window = LaunchRestoring("orders-service.clef");

        ToolbarButton(window, "Column view").AsButton().Invoke();

        var grid = WaitForTopLevelWindow("Columns");
        var table = UiHelpers.WaitFor(() => grid.TryByAutomationId("StructuredGrid"), "structured grid");
        Assert.True(UiHelpers.WaitUntil(() => table.FindAllChildren(cf => cf.ByControlType(ControlType.DataItem)).Length > 0),
            "The column view showed no rows.");
        Assert.True(UiHelpers.WaitUntil(() => table.FindFirstDescendant(cf => cf.ByControlType(ControlType.HeaderItem).And(cf.ByName("OrderId"))) is not null),
            "The most common property (OrderId) was not shown as a column.");

        string Status() => grid.TryByAutomationId("GridStatus")?.Name ?? string.Empty;
        Assert.True(UiHelpers.WaitUntil(() => Status().StartsWith("Showing", StringComparison.Ordinal)), $"Unexpected status '{Status()}'.");
        var before = Status();

        var search = UiHelpers.WaitFor(() => grid.FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit).And(cf.ByName("Search:"))), "search box").AsTextBox();
        search.Text = "SKU-881";

        Assert.True(UiHelpers.WaitUntil(() => Status() != before && Status().StartsWith("Showing", StringComparison.Ordinal)),
            $"Searching did not narrow the rows (status '{Status()}').");
    }

    [Fact]
    public void RowContextMenu_FilterByCorrelationId_FiltersToThatRequest()
    {
        var window = LaunchRestoring("checkout-service.log", "correlation");
        var list = UiHelpers.WaitFor(() => window.TryByAutomationId("LineListView"), "log list view");
        var rows = UiHelpers.WaitFor(() => list.FindAllChildren(cf => cf.ByControlType(ControlType.ListItem)).FirstOrDefault(), "first row");

        // Select + focus a row, then open its context menu from the keyboard (no synthetic mouse in RDP).
        rows.Patterns.SelectionItem.Pattern.Select();
        rows.Focus();
        Keyboard.TypeSimultaneously(VirtualKeyShort.SHIFT, VirtualKeyShort.F10);

        var desktop = _automation.GetDesktop();
        var correlation = UiHelpers.WaitFor(
            () => desktop.FindFirstDescendant(cf => cf.ByControlType(ControlType.MenuItem).And(cf.ByName("Filter by Correlation ID"))),
            "correlation menu item").AsMenuItem();
        Assert.True(correlation.IsEnabled, "The correlation submenu was disabled for a line with a request_id.");
        correlation.Expand();
        var firstId = UiHelpers.WaitFor(() => correlation.Items.FirstOrDefault(), "correlation id item").AsMenuItem();
        Assert.True(firstId.Name?.Contains("request_id", StringComparison.Ordinal) == true, $"Unexpected first id item: '{firstId.Name}' of [{string.Join(", ", correlation.Items.Select(i => i.Name))}]");
        firstId.Invoke();

        Assert.True(UiHelpers.WaitUntil(() => window.FindFirstDescendant(cf => cf.ByControlType(ControlType.Text)
                .And(cf.ByName("Filtered by: request_id ~ req-100"))) is not null
            || window.FindAllDescendants(cf => cf.ByControlType(ControlType.Text)).Any(t => t.Name?.Contains("request_id ~ req-100", StringComparison.Ordinal) == true)),
            "The status bar did not report the correlation filter.");
    }

    [Fact]
    public void RowContextMenu_AddNote_ShowsTheNoteGlyphOnTheLine()
    {
        var window = LaunchRestoring("checkout-service.log", "correlation");
        var list = UiHelpers.WaitFor(() => window.TryByAutomationId("LineListView"), "log list view");
        var row = UiHelpers.WaitFor(() => list.FindAllChildren(cf => cf.ByControlType(ControlType.ListItem)).FirstOrDefault(), "first row");

        row.Patterns.SelectionItem.Pattern.Select();
        row.Focus();
        Keyboard.TypeSimultaneously(VirtualKeyShort.SHIFT, VirtualKeyShort.F10);

        var desktop = _automation.GetDesktop();
        UiHelpers.WaitFor(
            () => desktop.FindFirstDescendant(cf => cf.ByControlType(ControlType.MenuItem).And(cf.ByName("Add / Edit Note…"))),
            "Add Note menu item").AsMenuItem().Invoke();

        var prompt = WaitForTopLevelWindow("Line Note");
        var input = UiHelpers.WaitFor(() => prompt.FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit)), "note text box");
        input.Patterns.Value.Pattern.SetValue("deploy started here");
        prompt.ByName("OK", ControlType.Button).AsButton().Invoke();

        Assert.True(UiHelpers.WaitUntil(() => row.FindFirstDescendant(cf => cf.ByName("Note")) is not null),
            "The note glyph did not appear on the annotated line.");
    }

    [Fact]
    public void LogList_FillsTheDocument_WhenNoLineIsSelected()
    {
        // Regression: with no selection the detail-panel row used to reserve ~220px of empty space.
        var window = LaunchRestoring("checkout-service.log", "correlation");
        var list = UiHelpers.WaitFor(() => window.TryByAutomationId("LineListView"), "log list view");
        Assert.True(UiHelpers.WaitUntil(() => list.FindAllChildren(cf => cf.ByControlType(ControlType.ListItem)).Length > 0));

        var windowHeight = window.BoundingRectangle.Height;
        var listHeight = list.BoundingRectangle.Height;
        Assert.True(listHeight > windowHeight * 0.7, $"The log list is only {listHeight}px of a {windowHeight}px window.");
    }

    [Fact]
    public void FilterViewsMenu_OffersSaveApplyAndDelete()
    {
        var window = LaunchRestoring("checkout-service.log", "correlation");

        var menu = UiHelpers.WaitFor(() => window.FindFirstDescendant(cf => cf.ByControlType(ControlType.MenuItem).And(cf.ByName("Filter views"))), "filter views menu").AsMenuItem();
        menu.Expand();
        var names = UiHelpers.WaitFor(() => menu.Items.Length >= 3 ? menu : null, "filter views items").AsMenuItem().Items.Select(i => i.Name).ToList();

        Assert.Contains("Save Current Filters as View…", names);
        Assert.Contains("Apply View", names);
        Assert.Contains("Delete View", names);
    }

    [Fact]
    public void FileMenu_OpensTheContainerLogsDialog_AndReportsTheCliState()
    {
        var window = LaunchRestoring("checkout-service.log", "correlation");

        UiHelpers.InvokeMenuPath(window, "File", "Open Container Logs (Docker / Kubernetes)...");

        var dialog = WaitForTopLevelWindow("Open Container Logs");
        Assert.NotNull(UiHelpers.WaitFor(() => dialog.FindFirstDescendant(cf => cf.ByName("Container / pod").And(cf.ByControlType(ControlType.ComboBox))), "target picker"));

        // Whether or not docker is installed here, the dialog must finish asking and say something (a list or an error).
        Assert.True(UiHelpers.WaitUntil(() => dialog.FindAllDescendants(cf => cf.ByControlType(ControlType.Text))
            .Any(t => t.Name.Contains("docker", StringComparison.OrdinalIgnoreCase) || t.Name.Contains("Nothing running", StringComparison.Ordinal))
            || dialog.FindFirstDescendant(cf => cf.ByControlType(ControlType.ComboBox))?.AsComboBox().Items.Length > 0));
        dialog.Close();
    }

    public void Dispose()
    {
        try
        {
            _app?.Close();
            _app?.Dispose();
        }
        catch (Exception)
        {
            // Best-effort — a failed test may have left the process in an odd state.
        }

        _automation?.Dispose();
        foreach (var disposable in _disposables)
        {
            disposable.Dispose();
        }
    }
}
