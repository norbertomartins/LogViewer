using CommunityToolkit.Mvvm.ComponentModel;
using LogViewer.Core.EventLogging;

namespace LogViewer.App.ViewModels;

public sealed partial class EventLogFilterRuleViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string? _providerName;

    [ObservableProperty]
    private string _regexPattern;

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private EventLogFilterField _field = EventLogFilterField.Message;

    public EventLogFilterRuleViewModel()
        : this(EventLogFilterRule.CreateDefault("New Filter", string.Empty))
    {
    }

    public EventLogFilterRuleViewModel(EventLogFilterRule rule)
    {
        Id = rule.Id;
        _name = rule.Name;
        _providerName = rule.ProviderName;
        _regexPattern = rule.RegexPattern;
        _isEnabled = rule.IsEnabled;
        _field = rule.Field;
    }

    public Guid Id { get; }

    public IReadOnlyList<EventLogFilterField> AvailableFields { get; } =
        [EventLogFilterField.Message, EventLogFilterField.ProviderName, EventLogFilterField.Level];

    public EventLogFilterRule ToRule() => new(Id, Name, string.IsNullOrWhiteSpace(ProviderName) ? null : ProviderName, RegexPattern, IsEnabled, Field);
}
