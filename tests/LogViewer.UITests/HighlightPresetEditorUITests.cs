using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Tools;
using FlaUI.UIA3;
using LogViewer.UITests.TestUtilities;

namespace LogViewer.UITests;

/// <summary>
/// End-to-end tests for Tools &gt; Highlight Presets..., the dialog used to create/edit the keyword and
/// regex rules that drive log-line highlighting — never previously covered by a UI test despite being
/// central to the highlight-color bugs this app has had.
/// </summary>
public sealed class HighlightPresetEditorUITests : IDisposable
{
    private readonly List<IDisposable> _disposables = [];
    private UIA3Automation _automation = null!;
    private Application _app = null!;

    private Window LaunchAndOpenEditor()
    {
        // Reuses the "small buffer + ERROR/WARN preset" settings fixture purely for its seeded
        // HighlightPresets — the ring buffer/log file it also sets up are irrelevant here, since this
        // dialog is reached from the main window's Tools menu and needs no document open at all.
        var path = AppExeLocator.Sample("timeline", "payments-service.log");
        var fixture = new IsolatedSettingsFixture(
            IsolatedSettingsFixture.RestoringFileWithSmallBufferAndHighlights(path, ringBufferCapacity: 500));
        _disposables.Add(fixture);

        _automation = new UIA3Automation();
        _app = Application.Launch(AppExeLocator.Find());
        var window = _app.GetMainWindow(_automation, UiHelpers.Timeout)
            ?? throw new TimeoutException("Main window did not appear.");
        UiHelpers.MaximizeWindow(window);

        UiHelpers.InvokeMenuPath(window, "Tools", "Highlight Presets...");

        return Retry.WhileNull(
            () => _automation.GetDesktop().FindFirstDescendant(cf => cf.ByControlType(ControlType.Window).And(cf.ByName("Manage Highlight Presets")))?.AsWindow(),
            UiHelpers.Timeout).Result
            ?? throw new TimeoutException("The Highlight Presets dialog did not appear.");
    }

    [Fact]
    public void OpeningTheEditor_ShowsTheSeededPresetWithItsRulesAutoSelected()
    {
        var editor = LaunchAndOpenEditor();

        var presetsList = UiHelpers.WaitFor(() => editor.TryByAutomationId("PresetsList"), "presets list").AsListBox();
        Assert.True(UiHelpers.WaitUntil(() => presetsList.Items.Length == 1), "Expected exactly the one seeded preset.");
        Assert.Equal("Errors & Exceptions", presetsList.Items[0].Text);

        var rulesList = UiHelpers.WaitFor(() => editor.TryByAutomationId("RulesList"), "rules list").AsListBox();
        Assert.True(UiHelpers.WaitUntil(() => rulesList.Items.Length == 2), "Expected the preset's two seeded rules.");
        Assert.Equal(["Error", "Warning"], rulesList.Items.Select(i => i.Text).ToArray());
    }

    [Fact]
    public void SelectingARule_ShowsItsPatternAndSeededColors()
    {
        var editor = LaunchAndOpenEditor();

        var rulesList = UiHelpers.WaitFor(() => editor.TryByAutomationId("RulesList"), "rules list").AsListBox();
        UiHelpers.WaitUntil(() => rulesList.Items.Length == 2);
        rulesList.Items[0].Select();

        var patternBox = UiHelpers.WaitFor(() => editor.TryByAutomationId("RulePatternBox"), "rule pattern box").AsTextBox();
        Assert.True(UiHelpers.WaitUntil(() => patternBox.Text == "ERROR"), $"Expected pattern 'ERROR'; got '{patternBox.Text}'.");

        var backgroundBox = editor.TryByAutomationId("LightBackgroundBox")!.AsTextBox();
        Assert.Equal("#C0392B", backgroundBox.Text);
    }

    [Fact]
    public void AddPreset_AddsANewEntryToThePresetsList()
    {
        var editor = LaunchAndOpenEditor();

        var presetsList = UiHelpers.WaitFor(() => editor.TryByAutomationId("PresetsList"), "presets list").AsListBox();
        UiHelpers.WaitUntil(() => presetsList.Items.Length == 1);

        UiHelpers.WaitFor(() => editor.TryByAutomationId("AddPresetButton"), "add-preset button").AsButton().Invoke();

        Assert.True(UiHelpers.WaitUntil(() => presetsList.Items.Length == 2),
            "Expected a new preset to be added to the list.");
    }

    [Fact]
    public void Cancel_ClosesTheDialog()
    {
        var editor = LaunchAndOpenEditor();

        UiHelpers.WaitFor(() => editor.TryByName("✖ Cancel", ControlType.Button), "Cancel button").AsButton().Invoke();

        Assert.True(UiHelpers.WaitUntil(() =>
                _automation.GetDesktop().FindFirstDescendant(cf => cf.ByControlType(ControlType.Window).And(cf.ByName("Manage Highlight Presets"))) is null),
            "The dialog did not close after Cancel.");
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
