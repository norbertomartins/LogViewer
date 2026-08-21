using LogViewer.App.ViewModels;
using LogViewer.Core.ExternalTools;

namespace LogViewer.App.Tests.ViewModels;

public sealed class ExternalToolViewModelTests
{
    [Fact]
    public void DefaultConstructor_WrapsANewDefaultToolWithAFreshId()
    {
        var viewModel = new ExternalToolViewModel();

        Assert.NotEqual(Guid.Empty, viewModel.Id);
        Assert.Equal("New Tool", viewModel.Name);
    }

    [Fact]
    public void ToDefinition_RoundTripsEditedValues()
    {
        var ruleId = Guid.NewGuid();
        var viewModel = new ExternalToolViewModel
        {
            Name = "Notepad",
            ExecutablePath = @"C:\Windows\notepad.exe",
            ArgumentTemplate = "{file}",
            ShortcutGesture = "Ctrl+Shift+N",
            AutoTriggerOnHighlightMatch = true,
            TriggerHighlightRuleId = ruleId,
        };

        var definition = viewModel.ToDefinition();

        Assert.Equal(viewModel.Id, definition.Id);
        Assert.Equal("Notepad", definition.Name);
        Assert.Equal(@"C:\Windows\notepad.exe", definition.ExecutablePath);
        Assert.Equal("{file}", definition.ArgumentTemplate);
        Assert.Equal("Ctrl+Shift+N", definition.ShortcutGesture);
        Assert.True(definition.AutoTriggerOnHighlightMatch);
        Assert.Equal(ruleId, definition.TriggerHighlightRuleId);
    }

    [Fact]
    public void ToDefinition_WhenShortcutGestureIsBlank_NormalizesToNull()
    {
        var viewModel = new ExternalToolViewModel { ShortcutGesture = "   " };

        var definition = viewModel.ToDefinition();

        Assert.Null(definition.ShortcutGesture);
    }

    [Fact]
    public void ToDefinition_WhenAutoTriggerIsDisabled_DropsTheTriggerRuleId()
    {
        var viewModel = new ExternalToolViewModel
        {
            AutoTriggerOnHighlightMatch = false,
            TriggerHighlightRuleId = Guid.NewGuid(),
        };

        var definition = viewModel.ToDefinition();

        Assert.Null(definition.TriggerHighlightRuleId);
    }
}
