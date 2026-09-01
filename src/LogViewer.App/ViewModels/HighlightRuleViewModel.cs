using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using LogViewer.App.Localization;
using LogViewer.Core.Highlighting;
using LogViewer.Core.Structured;

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
    private bool _isPatternValid = true;

    [ObservableProperty]
    private string? _targetProperty;

    /// <summary>Free-text sample the user pastes into the embedded tester; each line is matched live
    /// against the current pattern.</summary>
    [ObservableProperty]
    private string _testerInput = string.Empty;

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
        _targetProperty = rule.TargetProperty;
    }

    public static IReadOnlyList<string> WellKnownTargetProperties => StructuredFieldResolver.WellKnownFields;

    public Guid Id { get; }

    /// <summary>What the dark-theme preview swatch should show: the dark override if set, else the light color.</summary>
    public string EffectiveDarkForegroundHex => string.IsNullOrWhiteSpace(DarkForegroundHex) ? ForegroundHex : DarkForegroundHex;

    public string EffectiveDarkBackgroundHex => string.IsNullOrWhiteSpace(DarkBackgroundHex) ? BackgroundHex : DarkBackgroundHex;

    /// <summary>The pasted sample split into individual lines for the tester's per-line results list.</summary>
    public IReadOnlyList<string> TesterLines =>
        TesterInput.Length == 0 ? [] : TesterInput.Replace("\r\n", "\n").Split('\n');

    /// <summary>"3 / 10 lines match" — or an error note when the pattern is an invalid regex.</summary>
    public string TesterSummary
    {
        get
        {
            if (IsRegex && !IsPatternValid)
            {
                return Loc.Get("Common_InvalidRegex");
            }

            var lines = TesterLines;
            if (lines.Count == 0 || Pattern.Length == 0)
            {
                return string.Empty;
            }

            var hits = lines.Count(l => PatternMatchHelper.IsMatch(l, Pattern, IsRegex, IsCaseSensitive));
            return Loc.Format("Vm_Rule_Tester_Summary", hits, lines.Count);
        }
    }

    private void NotifyTesterChanged()
    {
        OnPropertyChanged(nameof(TesterLines));
        OnPropertyChanged(nameof(TesterSummary));
    }

    partial void OnTesterInputChanged(string value) => NotifyTesterChanged();

    partial void OnIsCaseSensitiveChanged(bool value) => NotifyTesterChanged();

    partial void OnPatternChanged(string value)
    {
        Validate();
        NotifyTesterChanged();
    }

    partial void OnIsRegexChanged(bool value)
    {
        Validate();
        NotifyTesterChanged();
    }

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

    public HighlightRule ToRule() => new(Id, Name, Pattern, IsRegex, IsCaseSensitive, IsEnabled, ForegroundHex, BackgroundHex, DarkForegroundHex, DarkBackgroundHex, TargetProperty);
}
