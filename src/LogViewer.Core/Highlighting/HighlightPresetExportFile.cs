namespace LogViewer.Core.Highlighting;

/// <summary>File format used to export/import one or more <see cref="HighlightPreset"/>s. Versioned independently
/// of <see cref="Configuration.AppSettings.SchemaVersion"/> since it's a standalone file, not part of settings.</summary>
public sealed record HighlightPresetExportFile(int FormatVersion, List<HighlightPreset> Presets)
{
    public const int CurrentFormatVersion = 1;
}
