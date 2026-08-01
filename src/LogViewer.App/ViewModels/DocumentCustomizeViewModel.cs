using CommunityToolkit.Mvvm.ComponentModel;

namespace LogViewer.App.ViewModels;

/// <summary>Backs the small dialog that sets a document's tab/MDI-title-bar color and glyph.</summary>
public sealed partial class DocumentCustomizeViewModel : ObservableObject
{
    [ObservableProperty]
    private string? _selectedColorHex;

    [ObservableProperty]
    private string? _selectedIconGlyph;

    public DocumentCustomizeViewModel(string? currentColorHex, string? currentIconGlyph)
    {
        _selectedColorHex = currentColorHex;
        _selectedIconGlyph = currentIconGlyph;
    }

    public IReadOnlyList<string?> AvailableColors { get; } =
        [null, "#3366CC", "#CC3333", "#33994C", "#CC8800", "#8833CC", "#008B8B", "#555555"];

    public IReadOnlyList<string?> AvailableGlyphs { get; } =
        [null, "⭐", "🔥", "⚠", "📌", "🔵", "✅", "🐞"];
}
