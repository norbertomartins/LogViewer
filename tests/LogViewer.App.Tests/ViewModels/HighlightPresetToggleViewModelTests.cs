using LogViewer.App.ViewModels;

namespace LogViewer.App.Tests.ViewModels;

public sealed class HighlightPresetToggleViewModelTests
{
    [Fact]
    public void Constructor_SetsIdNameAndInitialEnabledState()
    {
        var id = Guid.NewGuid();
        var toggle = new HighlightPresetToggleViewModel(id, "Errors", isEnabled: true);

        Assert.Equal(id, toggle.Id);
        Assert.Equal("Errors", toggle.Name);
        Assert.True(toggle.IsEnabled);
    }

    [Fact]
    public void SettingIsEnabled_RaisesEnabledChangedWithNewValue()
    {
        var toggle = new HighlightPresetToggleViewModel(Guid.NewGuid(), "Errors", isEnabled: false);
        HighlightPresetToggleViewModel? raisedSender = null;
        bool? raisedValue = null;
        toggle.EnabledChanged += (sender, value) => (raisedSender, raisedValue) = (sender, value);

        toggle.IsEnabled = true;

        Assert.Same(toggle, raisedSender);
        Assert.True(raisedValue);
    }

    [Fact]
    public void SettingIsEnabledToSameValue_DoesNotRaiseEnabledChanged()
    {
        var toggle = new HighlightPresetToggleViewModel(Guid.NewGuid(), "Errors", isEnabled: true);
        var raiseCount = 0;
        toggle.EnabledChanged += (_, _) => raiseCount++;

        toggle.IsEnabled = true;

        Assert.Equal(0, raiseCount);
    }
}
