using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using LogViewer.UITests.TestUtilities;

namespace LogViewer.UITests;

/// <summary>
/// End-to-end smoke tests for the "Open ..." source dialogs reachable from the File menu (SSH, HTTP,
/// process, ETW, Windows Event Log, merged sources) — previously never opened by any UI test. Each
/// dialog connects to something external (a remote host, a running process, an admin-only ETW session),
/// so these tests don't attempt an actual connection; they confirm the dialog is reachable, shows its
/// expected title, and Cancel closes it cleanly without opening a document.
/// </summary>
public sealed class OpenSourceDialogsUITests : IDisposable
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

    private Window OpenDialogAndAssertTitle(string menuEntry, string expectedTitle)
    {
        var window = Launch();
        UiHelpers.InvokeMenuPath(window, "File", menuEntry);

        return Retry.WhileNull(
            () => _automation.GetDesktop().FindFirstDescendant(cf => cf.ByControlType(ControlType.Window).And(cf.ByName(expectedTitle)))?.AsWindow(),
            UiHelpers.Timeout).Result
            ?? throw new TimeoutException($"The '{expectedTitle}' dialog did not appear.");
    }

    private void AssertCancelCloses(Window dialog, string expectedTitle)
    {
        UiHelpers.WaitFor(() => dialog.TryByName("Cancel", ControlType.Button), "Cancel button").AsButton().Invoke();

        Assert.True(
            UiHelpers.WaitUntil(() =>
                _automation.GetDesktop().FindFirstDescendant(cf => cf.ByControlType(ControlType.Window).And(cf.ByName(expectedTitle))) is null),
            $"The '{expectedTitle}' dialog did not close after Cancel.");
    }

    [Theory]
    [InlineData("Open S_SH Log Tail...", "Open SSH Log Tail")]
    [InlineData("Open _Remote Log Endpoint...", "Open Remote Log Endpoint")]
    [InlineData("Open _Command Output...", "Open Command Output")]
    [InlineData("Open _ETW Provider...", "Open ETW Provider")]
    [InlineData("Open _Windows Event Log...", "Open Windows Event Log")]
    [InlineData("Open _Merged Files / Folders (by time)...", "Open Merged Files / Folders (by time)")]
    public void Dialog_OpensWithExpectedTitle_AndCancelClosesIt(string menuEntryWithAccelerator, string expectedTitle)
    {
        // FlaUI's menu-item names come back with the "_" accelerator stripped by WPF's automation peer,
        // so the actual lookup name omits it — kept in the [InlineData] purely so the test data is
        // traceable back to the exact XAML/resx entry it exercises.
        var menuEntry = menuEntryWithAccelerator.Replace("_", "");
        var dialog = OpenDialogAndAssertTitle(menuEntry, expectedTitle);
        AssertCancelCloses(dialog, expectedTitle);
    }

    [Fact]
    public void OpenDirectoryWatch_WithAValidDirectory_OpensADocumentTab()
    {
        var window = Launch();
        var tempDir = Path.Combine(Path.GetTempPath(), $"logviewer-uitest-watch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            UiHelpers.InvokeMenuPath(window, "File", "Open Directory (Watch)...");
            var dialog = Retry.WhileNull(
                () => _automation.GetDesktop().FindFirstDescendant(cf => cf.ByControlType(ControlType.Window).And(cf.ByName("Open Directory (Watch)")))?.AsWindow(),
                UiHelpers.Timeout).Result
                ?? throw new TimeoutException("The 'Open Directory (Watch)' dialog did not appear.");

            var directoryBox = UiHelpers.WaitFor(() => dialog.TryByAutomationId("DirectoryPathBox"), "directory path box").AsTextBox();
            directoryBox.Text = tempDir;

            UiHelpers.WaitFor(() => dialog.TryByName("OK", ControlType.Button), "OK button").AsButton().Invoke();

            Assert.True(UiHelpers.WaitUntil(() => window.TryByAutomationId("LineListView") is not null),
                "Confirming a valid directory did not open a document.");
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
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
