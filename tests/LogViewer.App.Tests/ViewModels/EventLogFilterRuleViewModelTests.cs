using LogViewer.App.ViewModels;
using LogViewer.Core.EventLogging;

namespace LogViewer.App.Tests.ViewModels;

public sealed class EventLogFilterRuleViewModelTests
{
    [Fact]
    public void DefaultConstructor_WrapsANewDefaultRuleWithAFreshId()
    {
        var viewModel = new EventLogFilterRuleViewModel();

        Assert.NotEqual(Guid.Empty, viewModel.Id);
        Assert.Equal("New Filter", viewModel.Name);
        Assert.True(viewModel.IsEnabled);
        Assert.Equal(EventLogFilterField.Message, viewModel.Field);
    }

    [Fact]
    public void Constructor_FromExistingRule_CopiesAllFieldsIncludingId()
    {
        var rule = new EventLogFilterRule(Guid.NewGuid(), "Auth failures", "Security-Auditing", "fail(ed|ure)", true, EventLogFilterField.ProviderName);

        var viewModel = new EventLogFilterRuleViewModel(rule);

        Assert.Equal(rule.Id, viewModel.Id);
        Assert.Equal(rule.Name, viewModel.Name);
        Assert.Equal(rule.ProviderName, viewModel.ProviderName);
        Assert.Equal(rule.RegexPattern, viewModel.RegexPattern);
        Assert.Equal(rule.IsEnabled, viewModel.IsEnabled);
        Assert.Equal(rule.Field, viewModel.Field);
    }

    [Fact]
    public void ToRule_RoundTripsEditedValues()
    {
        var viewModel = new EventLogFilterRuleViewModel
        {
            Name = "Renamed",
            RegexPattern = "critical",
            IsEnabled = false,
            Field = EventLogFilterField.Level,
        };

        var rule = viewModel.ToRule();

        Assert.Equal(viewModel.Id, rule.Id);
        Assert.Equal("Renamed", rule.Name);
        Assert.Equal("critical", rule.RegexPattern);
        Assert.False(rule.IsEnabled);
        Assert.Equal(EventLogFilterField.Level, rule.Field);
    }

    [Fact]
    public void ToRule_WhenProviderNameIsBlank_NormalizesToNull()
    {
        var viewModel = new EventLogFilterRuleViewModel { ProviderName = "   " };

        var rule = viewModel.ToRule();

        Assert.Null(rule.ProviderName);
    }
}
