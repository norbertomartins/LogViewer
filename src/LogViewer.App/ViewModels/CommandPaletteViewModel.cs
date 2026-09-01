using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace LogViewer.App.ViewModels;

/// <summary>One selectable row in the command palette. <paramref name="Execute"/> is run on the UI
/// thread after the palette closes.</summary>
public sealed record PaletteCommand(string Title, string Category, Action Execute, string? Hint = null)
{
    public string DisplayCategory => Category;
}

public sealed partial class CommandPaletteViewModel : ObservableObject
{
    private readonly IReadOnlyList<PaletteCommand> _all;

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private PaletteCommand? _selected;

    public ObservableCollection<PaletteCommand> Results { get; } = [];

    public CommandPaletteViewModel(IReadOnlyList<PaletteCommand> commands)
    {
        _all = commands;
        Refresh();
    }

    partial void OnQueryChanged(string value) => Refresh();

    public void MoveSelection(int delta)
    {
        if (Results.Count == 0)
        {
            return;
        }

        var index = Selected is null ? 0 : Results.IndexOf(Selected);
        index = Math.Clamp(index + delta, 0, Results.Count - 1);
        Selected = Results[index];
    }

    private void Refresh()
    {
        var ranked = Rank(_all, Query);
        Results.Clear();
        foreach (var command in ranked)
        {
            Results.Add(command);
        }

        Selected = Results.Count > 0 ? Results[0] : null;
    }

    /// <summary>Case-insensitive ranking: exact substring in the title wins, then substring anywhere,
    /// then a subsequence ("fuzzy") match. Non-matches are dropped. Ties keep the input order.</summary>
    public static IReadOnlyList<PaletteCommand> Rank(IReadOnlyList<PaletteCommand> commands, string query)
    {
        query = query.Trim();
        if (query.Length == 0)
        {
            return commands;
        }

        var scored = new List<(PaletteCommand Command, int Score, int Ordinal)>();
        for (var i = 0; i < commands.Count; i++)
        {
            var score = Score(commands[i], query);
            if (score > int.MinValue)
            {
                scored.Add((commands[i], score, i));
            }
        }

        return scored
            .OrderByDescending(t => t.Score)
            .ThenBy(t => t.Ordinal)
            .Select(t => t.Command)
            .ToList();
    }

    private static int Score(PaletteCommand command, string query)
    {
        var title = command.Title;
        var haystack = $"{command.Category} {command.Title}";

        var titleIdx = title.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (titleIdx == 0)
        {
            return 1000;
        }

        if (titleIdx > 0)
        {
            return 800 - titleIdx;
        }

        var anyIdx = haystack.IndexOf(query, StringComparison.OrdinalIgnoreCase);
        if (anyIdx >= 0)
        {
            return 500 - anyIdx;
        }

        return IsSubsequence(haystack, query) ? 100 : int.MinValue;
    }

    private static bool IsSubsequence(string haystack, string needle)
    {
        var h = 0;
        foreach (var c in needle)
        {
            var found = false;
            while (h < haystack.Length)
            {
                if (char.ToLowerInvariant(haystack[h++]) == char.ToLowerInvariant(c))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return false;
            }
        }

        return true;
    }
}
