using System.IO;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using LogViewer.UITests.TestUtilities;

namespace LogViewer.UITests;

/// <summary>
/// End-to-end checks for the Phase 7 windows that had no UI coverage: Statistics, Compare Files (driving the
/// native Open dialog) and Export to File (driving the native Save dialog).
/// </summary>
public sealed class Phase7DialogsUITests : IDisposable
{
    private readonly List<IDisposable> _disposables = [];
    private readonly List<string> _tempFiles = [];
    private UIA3Automation _automation = null!;
    private Application _app = null!;

    private Window LaunchRestoring(string path)
    {
        var fixture = new IsolatedSettingsFixture(IsolatedSettingsFixture.RestoringFile(path));
        _disposables.Add(fixture);

        _automation = new UIA3Automation();
        _app = Application.Launch(AppExeLocator.Find());
        var window = _app.GetMainWindow(_automation, UiHelpers.Timeout)
            ?? throw new TimeoutException("Main window did not appear.");
        UiHelpers.MaximizeWindow(window);
        var list = UiHelpers.WaitFor(() => window.TryByAutomationId("LineListView"), "log list view");
        Assert.True(UiHelpers.WaitUntil(() => list.FindAllChildren(cf => cf.ByControlType(ControlType.ListItem)).Length > 0),
            "The restored document showed no lines.");
        return window;
    }

    // Owned windows (and the common file dialogs) nest under their owner in the UIA tree — search descendants.
    private Window WaitForWindow(string titlePrefix) =>
        UiHelpers.WaitFor(
            () => _automation.GetDesktop()
                .FindAllDescendants(cf => cf.ByControlType(ControlType.Window))
                .FirstOrDefault(w => w.Properties.Name.ValueOrDefault?.StartsWith(titlePrefix, StringComparison.Ordinal) == true),
            $"window '{titlePrefix}…'").AsWindow();

    /// <summary>Types a path into a common Open/Save dialog's file-name box and confirms it.</summary>
    private void CompleteFileDialog(string title, string path)
    {
        var dialog = UiHelpers.WaitFor(
            () => _automation.GetDesktop().FindAllDescendants(cf => cf.ByControlType(ControlType.Window).And(cf.ByClassName("#32770")))
                .FirstOrDefault(w => w.Properties.Name.ValueOrDefault == title),
            $"'{title}' dialog");
        var fileName = UiHelpers.WaitFor(
            () => dialog.FindFirstDescendant(cf => cf.ByControlType(ControlType.Edit).And(cf.ByName("File name:"))),
            "file name box");
        fileName.Patterns.Value.Pattern.SetValue(path);
        fileName.Focus();
        Keyboard.Press(VirtualKeyShort.RETURN);

        // Enter can be swallowed by the file-name autocomplete dropdown; fall back to the default button (id "1").
        if (!UiHelpers.WaitUntil(() => !IsAlive(fileName), TimeSpan.FromSeconds(3)))
        {
            dialog.FindFirstChild(cf => cf.ByAutomationId("1").And(cf.ByControlType(ControlType.Button)))?.AsButton().Invoke();
        }

        Assert.True(UiHelpers.WaitUntil(() => !IsAlive(fileName)), $"The '{title}' dialog did not close.");
    }

    // A closed dialog's cached elements keep reporting themselves as available (and the dialog's own node can linger
    // in the tree), but reading a pattern off one fails — use that as the "closed" signal.
    private static bool IsAlive(AutomationElement element)
    {
        try
        {
            _ = element.Patterns.Value.Pattern.Value.Value;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static IEnumerable<string> Texts(AutomationElement root) =>
        root.FindAllDescendants(cf => cf.ByControlType(ControlType.Text)).Select(t => t.Name ?? string.Empty);

    [Fact]
    public void Statistics_ShowsTotalsAndTopPatterns()
    {
        var window = LaunchRestoring(AppExeLocator.Sample("timeline", "payments-service.log"));

        UiHelpers.WaitFor(() => window.TryByName("Statistics", ControlType.Button), "Statistics button").AsButton().Invoke();

        var stats = WaitForWindow("Statistics");
        var patterns = UiHelpers.WaitFor(() => stats.FindFirstDescendant(cf => cf.ByControlType(ControlType.DataGrid).Or(cf.ByControlType(ControlType.List))), "patterns list");
        Assert.True(UiHelpers.WaitUntil(() => patterns.FindAllChildren(cf => cf.ByControlType(ControlType.DataItem).Or(cf.ByControlType(ControlType.ListItem))).Length > 0),
            $"The statistics window listed no patterns: {patterns.Properties.ControlType.ValueOrDefault} [{string.Join(" | ", patterns.FindAllChildren().Select(c => $"{c.Properties.ControlType.ValueOrDefault}:{c.Properties.Name.ValueOrDefault}"))}] texts=[{string.Join(" | ", Texts(stats))}]");
        Assert.Contains(Texts(stats), t => t == "Total Lines");
        stats.Close();
    }

    [Fact]
    public void CompareFiles_AlignsTwoChosenFiles()
    {
        var v1 = AppExeLocator.Sample("block-diff", "01-correlation-template", "v1.log");
        var v2 = AppExeLocator.Sample("block-diff", "01-correlation-template", "v2.log");
        var window = LaunchRestoring(v1);

        UiHelpers.InvokeMenuPath(window, "File", "Compare Files...");
        var compare = WaitForWindow("Compare Files");

        AutomationElement[] buttons = [];
        Assert.True(UiHelpers.WaitUntil(() =>
            (buttons = compare.FindAllDescendants(cf => cf.ByControlType(ControlType.Button).And(cf.ByName("Browse…")))).Length == 2),
            "Expected two Browse buttons.");
        buttons[0].AsButton().Invoke();
        CompleteFileDialog("Open", v1);
        buttons[1].AsButton().Invoke();
        CompleteFileDialog("Open", v2);

        compare.ByName("Compare", ControlType.Button).AsButton().Invoke();

        Assert.True(UiHelpers.WaitUntil(() => Texts(compare).Any(t => t.Contains("aligned", StringComparison.Ordinal))),
            $"The comparison did not finish: [{string.Join(" | ", Texts(compare))}]");
        var rows = compare.FindFirstDescendant(cf => cf.ByControlType(ControlType.DataGrid))!
            .FindAllChildren(cf => cf.ByControlType(ControlType.DataItem));
        Assert.NotEmpty(rows);
        compare.Close();
    }

    [Fact]
    public void ExportToFile_WritesTheVisibleLines()
    {
        var source = AppExeLocator.Sample("correlation", "checkout-service.log");
        var window = LaunchRestoring(source);
        var target = Path.Combine(Path.GetTempPath(), $"logviewer-uitest-export-{Guid.NewGuid():N}.log");
        _tempFiles.Add(target);

        var export = UiHelpers.WaitFor(
            () => window.FindFirstDescendant(cf => cf.ByControlType(ControlType.MenuItem).And(cf.ByName("Export"))),
            "Export menu").AsMenuItem();
        export.Expand();
        UiHelpers.WaitFor(() => export.Items.FirstOrDefault(i => i.Name == "Export to File…"), "Export to File item").AsMenuItem().Invoke();

        CompleteFileDialog("Save As", target);

        Assert.True(UiHelpers.WaitUntil(() => File.Exists(target) && new FileInfo(target).Length > 0), "The export file was not written.");
        var exported = File.ReadAllLines(target);
        var original = File.ReadAllLines(source);
        Assert.Equal(original.Length, exported.Length);
        Assert.Equal(original[0], exported[0]);
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

        foreach (var file in _tempFiles)
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
        }
    }
}
