using System.Globalization;

namespace LogViewer.Core.Structured;

/// <summary>A property seen in structured events, with how many of them carry it.</summary>
public sealed record StructuredPropertyUsage(string Name, int Count);

/// <summary>Keeps (or, with <paramref name="Exclude"/>, drops) the events whose <paramref name="Field"/> — a property
/// name or one of <see cref="StructuredFieldResolver"/>'s pseudo-fields — equals <paramref name="Value"/>.</summary>
public sealed record StructuredValueFilter(string Field, string? Value, bool Exclude = false)
{
    public bool Matches(StructuredLogEvent evt) =>
        string.Equals(StructuredFieldResolver.Resolve(evt, Field), Value, StringComparison.Ordinal) != Exclude;

    /// <summary>"Field = value" / "Field ≠ value" ("∅" for a missing value), for display.</summary>
    public string Description => $"{Field} {(Exclude ? "≠" : "=")} {Value ?? "∅"}";
}

/// <summary>Helpers for showing structured events as a table: which properties exist, and how values sort.</summary>
public static class StructuredColumns
{
    /// <summary>Every property name in <paramref name="events"/>, most common first (ties by name).</summary>
    public static IReadOnlyList<StructuredPropertyUsage> DiscoverProperties(IEnumerable<StructuredLogEvent> events)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var evt in events)
        {
            foreach (var name in evt.Properties.Keys)
            {
                counts[name] = counts.GetValueOrDefault(name) + 1;
            }
        }

        return [.. counts
            .Select(kv => new StructuredPropertyUsage(kv.Key, kv.Value))
            .OrderByDescending(u => u.Count)
            .ThenBy(u => u.Name, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>Orders property values the way a person expects: numbers (and durations like "12.5") numerically, then
    /// everything else case-insensitively; missing values last.</summary>
    public static int CompareValues(string? x, string? y)
    {
        if (x is null || y is null)
        {
            return x is null ? (y is null ? 0 : 1) : -1;
        }

        var xIsNumber = double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out var xNumber);
        var yIsNumber = double.TryParse(y, NumberStyles.Float, CultureInfo.InvariantCulture, out var yNumber);
        if (xIsNumber && yIsNumber)
        {
            return xNumber.CompareTo(yNumber);
        }

        if (xIsNumber != yIsNumber)
        {
            return xIsNumber ? -1 : 1;
        }

        var result = string.Compare(x, y, StringComparison.OrdinalIgnoreCase);
        return result != 0 ? result : string.CompareOrdinal(x, y);
    }
}
