using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using LogViewer.UITests.TestUtilities;

namespace LogViewer.UITests;

/// <summary>
/// End-to-end smoke tests for the remaining Tools-menu dialogs that had no UI coverage: External Tools,
/// Windows Services, the Command Palette, and Theme Manager (nested inside Settings).
/// </summary>
public sealed class MiscDialogsUITests : IDisposable
{
    private readonly List<IDisposable> _disposables = [];
    private UIA3Automation _automation = null!;
    private Application _app = null!;

    private Window Launch()
    {
        var fixture = new IsolatedSettingsFixture();
        _disposables.Add(fixture);

        _automation = new UIA3Automation();
        _app = Application.Launch(AppExeLocator.Find());
        var window = _app.GetMainWindow(_automation, UiHelpers.Timeout)
            ?? throw new TimeoutException("Main window did not appear.");
        UiHelpers.MaximizeWindow(window);
        return window;
    }

    private Window FindDialogByTitle(string title) =>
        Retry.WhileNull(
            () => _automation.GetDesktop().FindFirstDescendant(cf => cf.ByControlType(ControlType.Window).And(cf.ByName(title)))?.AsWindow(),
            UiHelpers.Timeout).Result
        ?? throw new TimeoutException($"The '{title}' dialog did not appear.");

    [Theory]
    [InlineData("External _Tools...", "External Tools", "Cancel")]
    [InlineData("_Windows Services...", "Windows Services", "Close")]
    public void ToolsMenuDialog_OpensWithExpectedTitle_AndItsCloseButtonWorks(string menuEntryWithAccelerator, string expectedTitle, string closeButtonName)
    {
        var window = Launch();
        UiHelpers.InvokeMenuPath(window, "Tools", menuEntryWithAccelerator.Replace("_", ""));
        var dialog = FindDialogByTitle(expectedTitle);

        UiHelpers.WaitFor(() => dialog.TryByName(closeButtonName, ControlType.Button), $"{closeButtonName} button").AsButton().Invoke();

        Assert.True(
            UiHelpers.WaitUntil(() => _automation.GetDesktop().FindFirstDescendant(cf => cf.ByControlType(ControlType.Window).And(cf.ByName(expectedTitle))) is null),
            $"The '{expectedTitle}' dialog did not close.");
    }

    [Fact]
    public void ThemeManager_OpensFromSettings_AndListsTheBuiltInThemes()
    {
        var window = Launch();
        UiHelpers.InvokeMenuPath(window, "Tools", "Settings...");
        var settings = FindDialogByTitle("Settings");

        UiHelpers.WaitFor(() => settings.TryByName("Manage Themes...", ControlType.Button), "Manage Themes button").AsButton().Invoke();
        var themeManager = FindDialogByTitle("Manage Themes");

        var themeList = themeManager.FindFirstDescendant(cf => cf.ByControlType(ControlType.List));
        Assert.NotNull(themeList);
        Assert.True(
            UiHelpers.WaitUntil(() => themeList.FindAllChildren(cf => cf.ByControlType(ControlType.ListItem)).Length >= 2),
            "Expected at least the two built-in themes (Light, Dark) to be listed.");

        UiHelpers.WaitFor(() => themeManager.TryByName("Cancel", ControlType.Button), "Theme Manager Cancel button").AsButton().Invoke();
        Assert.True(UiHelpers.WaitUntil(() =>
                _automation.GetDesktop().FindFirstDescendant(cf => cf.ByControlType(ControlType.Window).And(cf.ByName("Manage Themes"))) is null),
            "The Theme Manager dialog did not close.");

        UiHelpers.WaitFor(() => settings.TryByName("Cancel", ControlType.Button), "Settings Cancel button").AsButton().Invoke();
    }

    [Fact]
    public void CommandPalette_OpensAndFiltersResultsAsYouType()
    {
        var window = Launch();
        UiHelpers.InvokeMenuPath(window, "Tools", "Command Palette...");

        var palette = Retry.WhileNull(
            () => _automation.GetDesktop().FindFirstDescendant(cf => cf.ByAutomationId("CommandPalette"))?.AsWindow(),
            UiHelpers.Timeout).Result
            ?? throw new TimeoutException("The Command Palette did not appear.");

        var queryBox = UiHelpers.WaitFor(() => palette.TryByAutomationId("QueryBox"), "palette query box").AsTextBox();
        var resultList = UiHelpers.WaitFor(() => palette.TryByAutomationId("ResultList"), "palette result list").AsListBox();

        Assert.True(UiHelpers.WaitUntil(() => resultList.Items.Length > 0), "The palette showed no commands at all before filtering.");
        var unfilteredCount = resultList.Items.Length;

        // "Open" matches several commands (Open File, Open Directory, ...) but not all of them — with
        // no document open yet, document-scoped commands like bookmarking or exporting aren't even in
        // the unfiltered list, so this should narrow it rather than leave every command visible.
        queryBox.Text = "Open";

        Assert.True(
            UiHelpers.WaitUntil(() => resultList.Items.Length > 0 && resultList.Items.Length < unfilteredCount),
            $"Expected filtering to narrow the {unfilteredCount} unfiltered commands; got {resultList.Items.Length}.");

        // Closed via the Window automation pattern (Close()), not the Escape key, to avoid synthesized
        // keyboard input — consistent with how every other test in this suite drives the app.
        palette.Close();
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
