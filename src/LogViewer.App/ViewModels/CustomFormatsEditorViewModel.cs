using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LogViewer.App.Localization;
using LogViewer.Core.Structured;

namespace LogViewer.App.ViewModels;

/// <summary>One editable custom format in the editor list.</summary>
public sealed partial class CustomFormatItemViewModel : ObservableObject
{
    public CustomFormatItemViewModel(CustomLogFormat format)
    {
        Id = format.Id;
        _name = format.Name;
        _pattern = format.Pattern;
        _ignoreCase = format.IgnoreCase;
        _timestampFormat = format.TimestampFormat;
        _timestampIsUtc = format.TimestampIsUtc;
        _joinContinuationLines = format.JoinContinuationLines;
        Validate();
    }

    public string Id { get; }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _pattern;

    [ObservableProperty]
    private bool _ignoreCase;

    [ObservableProperty]
    private string? _timestampFormat;

    [ObservableProperty]
    private bool _timestampIsUtc;

    [ObservableProperty]
    private bool _joinContinuationLines;

    /// <summary>Why the pattern can't be used, or null when it compiles and has named groups.</summary>
    [ObservableProperty]
    private string? _error;

    public bool IsValid => Error is null && !string.IsNullOrWhiteSpace(Name);

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(IsValid));

    partial void OnPatternChanged(string value) => Validate();

    partial void OnIgnoreCaseChanged(bool value) => Validate();

    private void Validate()
    {
        Error = RegexLogLineParser.TryCreate(ToFormat(), out _, out var error) ? null : error;
        OnPropertyChanged(nameof(IsValid));
    }

    public CustomLogFormat ToFormat() => new()
    {
        Id = Id,
        Name = Name.Trim(),
        Pattern = Pattern,
        IgnoreCase = IgnoreCase,
        TimestampFormat = string.IsNullOrWhiteSpace(TimestampFormat) ? null : TimestampFormat.Trim(),
        TimestampIsUtc = TimestampIsUtc,
        JoinContinuationLines = JoinContinuationLines,
    };
}

/// <summary>How one sample line fared against the selected format, for the editor's live preview grid.</summary>
public sealed record SampleParseRow(string Line, bool Matched, string? Timestamp, string? Level, string? Message, string? Properties);

/// <summary>
/// Backs the modal "Custom Log Formats" editor: add/duplicate/remove regex formats, start from a bundled example,
/// and see live how every sample line (prefilled from the active document) parses — timestamp, level, message
/// and extra properties — before saving.
/// </summary>
public sealed partial class CustomFormatsEditorViewModel : ObservableObject
{
    public CustomFormatsEditorViewModel(IEnumerable<CustomLogFormat> formats, IEnumerable<string>? sampleLines = null)
    {
        Formats = new ObservableCollection<CustomFormatItemViewModel>(formats.Select(f => new CustomFormatItemViewModel(f)));
        foreach (var item in Formats)
        {
            item.PropertyChanged += OnItemChanged;
        }

        _sampleText = string.Join(Environment.NewLine, sampleLines ?? []);
        SelectedFormat = Formats.FirstOrDefault();
    }

    public ObservableCollection<CustomFormatItemViewModel> Formats { get; }

    public IReadOnlyList<CustomLogFormat> Examples => CustomLogFormatExamples.All;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveFormatCommand))]
    [NotifyCanExecuteChangedFor(nameof(DuplicateFormatCommand))]
    private CustomFormatItemViewModel? _selectedFormat;

    [ObservableProperty]
    private string _sampleText;

    [ObservableProperty]
    private string? _sampleSummary;

    public ObservableCollection<SampleParseRow> SampleResults { get; } = [];

    public bool CanSave => Formats.All(f => f.IsValid);

    partial void OnSelectedFormatChanged(CustomFormatItemViewModel? value) => RecomputeSample();

    partial void OnSampleTextChanged(string value) => RecomputeSample();

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(CanSave));
        if (ReferenceEquals(sender, SelectedFormat))
        {
            RecomputeSample();
        }
    }

    private void RecomputeSample()
    {
        SampleResults.Clear();
        var lines = SampleText.Replace("\r\n", "\n").Split('\n').Where(l => l.Length > 0).ToList();
        if (SelectedFormat is null || lines.Count == 0)
        {
            SampleSummary = null;
            return;
        }

        if (!RegexLogLineParser.TryCreate(SelectedFormat.ToFormat(), out var parser, out _))
        {
            SampleSummary = SelectedFormat.Error;
            return;
        }

        var matched = 0;
        foreach (var line in lines)
        {
            if (parser!.TryParse(line, out var evt) && evt is not null)
            {
                matched++;
                SampleResults.Add(new SampleParseRow(
                    line,
                    true,
                    evt.Timestamp?.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", System.Globalization.CultureInfo.InvariantCulture),
                    evt.Level,
                    evt.RenderedMessage,
                    string.Join(", ", evt.Properties.Select(p => $"{p.Key}={p.Value}"))));
            }
            else
            {
                SampleResults.Add(new SampleParseRow(line, false, null, null, null, null));
            }
        }

        SampleSummary = Loc.Format("Vm_Formats_SampleSummary", matched, lines.Count);
    }

    [RelayCommand]
    private void AddFormat() => Add(new CustomLogFormat
    {
        Name = Loc.Get("Vm_Formats_NewName"),
        Pattern = @"^(?<timestamp>\S+ \S+) (?<level>\w+) (?<message>.*)$",
        JoinContinuationLines = true,
    });

    [RelayCommand]
    private void AddFromExample(CustomLogFormat? example)
    {
        if (example is not null)
        {
            var copy = example.Clone();
            copy.Id = new CustomLogFormat().Id;
            Add(copy);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DuplicateFormat()
    {
        if (SelectedFormat is null)
        {
            return;
        }

        var copy = SelectedFormat.ToFormat();
        copy.Id = new CustomLogFormat().Id;
        copy.Name = Loc.Format("Vm_Formats_CopyName", copy.Name);
        Add(copy);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void RemoveFormat()
    {
        if (SelectedFormat is not { } item)
        {
            return;
        }

        var index = Formats.IndexOf(item);
        item.PropertyChanged -= OnItemChanged;
        Formats.Remove(item);
        SelectedFormat = Formats.Count == 0 ? null : Formats[Math.Min(index, Formats.Count - 1)];
        OnPropertyChanged(nameof(CanSave));
    }

    private bool HasSelection() => SelectedFormat is not null;

    private void Add(CustomLogFormat format)
    {
        var item = new CustomFormatItemViewModel(format);
        item.PropertyChanged += OnItemChanged;
        Formats.Add(item);
        SelectedFormat = item;
        OnPropertyChanged(nameof(CanSave));
    }

    public IReadOnlyList<CustomLogFormat> ToFormats() => [.. Formats.Select(f => f.ToFormat())];
}
