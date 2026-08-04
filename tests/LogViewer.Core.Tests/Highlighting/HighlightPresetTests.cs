using LogViewer.Core.Highlighting;

namespace LogViewer.Core.Tests.Highlighting;

public sealed class HighlightPresetTests
{
    [Fact]
    public void FlattenForMatching_SkipsDisabledPresets()
    {
        var enabledRule = HighlightRule.CreateDefault("Errors", "ERROR");
        var disabledRule = HighlightRule.CreateDefault("Warnings", "WARN");
        var presets = new List<HighlightPreset>
        {
            new() { Name = "Enabled", IsEnabled = true, Rules = [enabledRule] },
            new() { Name = "Disabled", IsEnabled = false, Rules = [disabledRule] },
        };

        var flattened = HighlightPreset.FlattenForMatching(presets);

        var rule = Assert.Single(flattened);
        Assert.Equal(enabledRule.Id, rule.Id);
    }

    [Fact]
    public void FlattenForMatching_SkipsDisabledRulesWithinEnabledPreset()
    {
        var enabledRule = HighlightRule.CreateDefault("Errors", "ERROR");
        var disabledRule = HighlightRule.CreateDefault("Warnings", "WARN") with { IsEnabled = false };
        var presets = new List<HighlightPreset>
        {
            new() { Name = "Preset", IsEnabled = true, Rules = [enabledRule, disabledRule] },
        };

        var flattened = HighlightPreset.FlattenForMatching(presets);

        var rule = Assert.Single(flattened);
        Assert.Equal(enabledRule.Id, rule.Id);
    }

    [Fact]
    public void FlattenForMatching_PreservesPresetThenRuleListOrder()
    {
        var presetARule1 = HighlightRule.CreateDefault("A1", "a1");
        var presetARule2 = HighlightRule.CreateDefault("A2", "a2");
        var presetBRule1 = HighlightRule.CreateDefault("B1", "b1");
        var presetBRule2 = HighlightRule.CreateDefault("B2", "b2");
        var presets = new List<HighlightPreset>
        {
            new() { Name = "A", IsEnabled = true, Rules = [presetARule1, presetARule2] },
            new() { Name = "B", IsEnabled = true, Rules = [presetBRule1, presetBRule2] },
        };

        var flattened = HighlightPreset.FlattenForMatching(presets);

        Assert.Equal(
            [presetARule1.Id, presetARule2.Id, presetBRule1.Id, presetBRule2.Id],
            flattened.Select(r => r.Id));
    }

    [Fact]
    public void Duplicate_AssignsNewIdsToPresetAndAllRules()
    {
        var original = new HighlightPreset
        {
            Name = "Original",
            Rules = [HighlightRule.CreateDefault("Errors", "ERROR")],
        };

        var copy = original.Duplicate("Original Copy");

        Assert.NotEqual(original.Id, copy.Id);
        Assert.NotEqual(original.Rules[0].Id, copy.Rules[0].Id);
        Assert.Equal("Original Copy", copy.Name);
        Assert.Equal(original.Rules[0].Pattern, copy.Rules[0].Pattern);
    }
}
