namespace LogViewer.Core.Highlighting;

/// <summary>A named, independently toggleable group of <see cref="HighlightRule"/>s. Multiple presets can be
/// enabled at once (e.g. a general-purpose preset alongside a feature-specific one). Match precedence on overlap
/// is determined purely by list position — see <see cref="FlattenForMatching"/>.</summary>
public sealed class HighlightPreset
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "New Preset";

    public bool IsEnabled { get; set; } = true;

    public List<HighlightRule> Rules { get; set; } = [];

    public static HighlightPreset CreateDefault(string name) => new() { Name = name };

    /// <summary>Copies this preset, assigning a fresh <see cref="Id"/> to the preset and to every rule in it, so
    /// the copy never collides with the original in <see cref="HighlightEngine"/>'s per-rule regex cache or in
    /// an external tool's trigger-rule reference.</summary>
    public HighlightPreset Duplicate(string newName) => new()
    {
        Name = newName,
        IsEnabled = IsEnabled,
        Rules = Rules.Select(r => r with { Id = Guid.NewGuid() }).ToList(),
    };

    /// <summary>Enabled presets in list order, then enabled rules within each preset in list order — this is the
    /// single source of truth for match precedence; the first rule in the result to match a line wins.</summary>
    public static IReadOnlyList<HighlightRule> FlattenForMatching(IEnumerable<HighlightPreset> presets) =>
        presets.Where(p => p.IsEnabled).SelectMany(p => p.Rules.Where(r => r.IsEnabled)).ToList();
}
