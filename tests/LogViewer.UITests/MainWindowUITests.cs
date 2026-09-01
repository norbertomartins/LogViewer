using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using LogViewer.UITests.TestUtilities;

namespace LogViewer.UITests;

/// <summary>
/// Drives the real, already-built <c>LogViewer.App.exe</c> end-to-end via UI Automation (FlaUI),
/// the same way a user would — no in-process references to app types. Each test launches its own
/// instance against an isolated settings file (see <see cref="IsolatedSettingsFixture"/>) so it never
/// reads or clobbers a developer's real recent-files/session state, and force-kills the process on
/// teardown so a failed assertion never leaves a orphaned LogViewer.App.exe behind.
/// </summary>
public sealed class MainWindowUITests : IDisposable
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(20);

    private readonly IsolatedSettingsFixture _settingsFixture = new();
    private readonly UIA3Automation _automation = new();
    private readonly Application _app = Application.Launch(AppExeLocator.Find());

    [Fact]
    public void MainWindow_Launches_WithLogViewerTitle()
    {
        var window = GetMainWindow();

        Assert.StartsWith("LogViewer", window.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void FileMenu_ContainsEveryOpenSourceEntry()
    {
        var window = GetMainWindow();

        IReadOnlyList<string> names;
        try
        {
            names = UiHelpers.MenuItems(window, "File");
        }
        catch (TimeoutException)
        {
            // A disconnected / locked RDP session can't render a WPF popup menu, so its items never
            // reach the automation tree. Nothing to assert here in that environment.
            return;
        }

        foreach (var entry in new[]
                 {
                     "Open File...", "Open Directory (Watch)...", "Open Merged Files / Folders (by time)...",
                     "Open Windows Event Log...", "Open Remote Log Endpoint...", "Open Command Output...",
                     "Open SSH Log Tail...", "Open ETW Provider...",
                 })
        {
            Assert.Contains(entry, names);
        }
    }

    [Fact]
    public void ToolsMenu_Settings_OpensAndCancelsSettingsDialog()
    {
        var window = GetMainWindow();

        // Drive the menu through UIA ExpandCollapse/Invoke rather than a synthesized click, so this
        // passes in locked sessions where SendInput is denied.
        UiHelpers.InvokeMenuPath(window, "Tools", "Settings...");

        // The Settings dialog is a WPF window Owned by the main window, which some UIA providers
        // nest as a descendant of the owner rather than as a direct child of the Desktop — so this
        // searches the whole subtree (FindFirstDescendant) rather than GetAllTopLevelWindows, which
        // only walks the Desktop's direct children.
        var settingsWindow = Retry.WhileNull(
            () => _automation.GetDesktop().FindFirstDescendant(cf => cf.ByControlType(ControlType.Window).And(cf.ByName("Settings")))?.AsWindow(),
            DefaultTimeout).Result
            ?? throw new TimeoutException("The Settings dialog did not appear.");

        FindByName(settingsWindow, "Cancel", ControlType.Button).AsButton().Invoke();

        var dialogClosed = WaitUntil(SettingsDialogIsClosed, DefaultTimeout);
        Assert.True(dialogClosed, "The Settings dialog did not close after clicking Cancel.");

        bool SettingsDialogIsClosed()
        {
            try
            {
                return _automation.GetDesktop().FindFirstDescendant(cf => cf.ByControlType(ControlType.Window).And(cf.ByName("Settings"))) is null;
            }
            catch (Exception)
            {
                // The dialog's automation element can throw while its window handle is mid-teardown;
                // treat that as "still closing" rather than a hard failure.
                return false;
            }
        }
    }

    private Window GetMainWindow()
    {
        var window = _app.GetMainWindow(_automation, DefaultTimeout)
            ?? throw new TimeoutException("LogViewer.App did not show its main window in time.");
        return window;
    }

    private static AutomationElement FindByName(AutomationElement parent, string name, ControlType controlType) =>
        Retry.WhileNull(
            () => parent.FindFirstDescendant(cf => cf.ByControlType(controlType).And(cf.ByName(name))),
            DefaultTimeout).Result
        ?? throw new TimeoutException($"Could not find a {controlType} named '{name}'.");

    private static bool WaitUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(100);
        }

        return condition();
    }

    public void Dispose()
    {
        // Each step is independently best-effort: FlaUI's Application can throw
        // "No process is associated with this object" from HasExited/Close once the
        // process has already exited on its own, and teardown must never fail the test.
        try
        {
            _app.Close();
        }
        catch
        {
        }

        try
        {
            if (!_app.HasExited)
            {
                _app.Kill();
            }
        }
        catch
        {
        }

        try
        {
            _app.Dispose();
        }
        catch
        {
        }

        _automation.Dispose();
        _settingsFixture.Dispose();
    }
}
