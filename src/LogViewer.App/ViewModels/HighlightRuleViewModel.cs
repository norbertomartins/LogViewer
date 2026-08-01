using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using LogViewer.Core.Highlighting;

namespace LogViewer.App.ViewModels;

/// <summary>Editable wrapper around a <see cref="HighlightRule"/> for the rule-editor dialog, with live regex validation.</summary>
public sealed partial class HighlightRuleViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _pattern;

    [ObservableProperty]
    private bool _isRegex;

    [ObservableProperty]
    private bool _isCaseSensitive;

    [ObservableProperty]
    private bool _isEnabled = true;

    [ObservableProperty]
    private string _foregroundHex;

    [ObservableProperty]
    private string _backgroundHex;

    /// <summary>Overrides <see cref="ForegroundHex"/> for dark-based themes; blank means "same as light".</summary>
    [ObservableProperty]
    private string? _darkForegroundHex;

    /// <summary>Overrides <see cref="BackgroundHex"/> for dark-based themes; blank means "same as light".</summary>
    [ObservableProperty]
    private string? _darkBackgroundHex;

    [ObservableProperty]
    private int _priority;

    [ObservableProperty]
    private bool _isPatternValid = true;

    public HighlightRuleViewModel()
        : this(HighlightRule.CreateDefault("New Rule", string.Empty))
    {
    }

    public HighlightRuleViewModel(HighlightRule rule)
    {
        Id = rule.Id;
        _name = rule.Name;
        _pattern = rule.Pattern;
        _isRegex = rule.IsRegex;
        _isCaseSensitive = rule.IsCaseSensitive;
        _isEnabled = rule.IsEnabled;
        _foregroundHex = rule.ForegroundHex;
        _backgroundHex = rule.BackgroundHex;
        _darkForegroundHex = rule.DarkForegroundHex;
        _darkBackgroundHex = rule.DarkBackgroundHex;
        _priority = rule.Priority;
    }

    public Guid Id { get; }

    /// <summary>What the dark-theme preview swatch should show: the dark override if set, else the light color.</summary>
    public string EffectiveDarkForegroundHex => string.IsNullOrWhiteSpace(DarkForegroundHex) ? ForegroundHex : DarkForegroundHex;

    public string EffectiveDarkBackgroundHex => string.IsNullOrWhiteSpace(DarkBackgroundHex) ? BackgroundHex : DarkBackgroundHex;

    partial void OnPatternChanged(string value) => Validate();

    partial void OnIsRegexChanged(bool value) => Validate();

    partial void OnForegroundHexChanged(string value) => OnPropertyChanged(nameof(EffectiveDarkForegroundHex));

    partial void OnBackgroundHexChanged(string value) => OnPropertyChanged(nameof(EffectiveDarkBackgroundHex));

    partial void OnDarkForegroundHexChanged(string? value) => OnPropertyChanged(nameof(EffectiveDarkForegroundHex));

    partial void OnDarkBackgroundHexChanged(string? value) => OnPropertyChanged(nameof(EffectiveDarkBackgroundHex));

    private void Validate()
    {
        if (!IsRegex || string.IsNullOrEmpty(Pattern))
        {
            IsPatternValid = true;
            return;
        }

        try
        {
            _ = new Regex(Pattern);
            IsPatternValid = true;
        }
        catch (ArgumentException)
        {
            IsPatternValid = false;
        }
    }

    public HighlightRule ToRule() => new(Id, Name, Pattern, IsRegex, IsCaseSensitive, IsEnabled, ForegroundHex, BackgroundHex, Priority, DarkForegroundHex, DarkBackgroundHex);
}
