using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;

namespace LogViewer.App.ViewModels;

/// <summary>
/// One entry in the merged-sources list: either a concrete file, or a folder whose matching files are
/// expanded when the dialog is accepted. Folders let a user merge logs that live in different
/// directories (or several whole directories) into a single time-ordered view.
/// </summary>
public sealed record MergeSourceEntry(bool IsFolder, string Path, string? Pattern)
{
    public string Display => IsFolder ? System.IO.Path.Combine(Path, Pattern ?? "*.log") : Path;

    public string Kind => IsFolder ? "Folder" : "File";
}

public sealed partial class OpenMergedSourcesViewModel : ObservableObject
{
    [ObservableProperty]
    private string _folderPattern = "*.log";

    public ObservableCollection<MergeSourceEntry> Entries { get; } = [];

    public OpenMergedSourcesViewModel()
    {
        Entries.CollectionChanged += (_, _) => OnPropertyChanged(nameof(IsValid));
    }

    [RelayCommand]
    private void AddFiles()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Add files to merge",
            Filter = "Log files (*.log;*.txt)|*.log;*.txt|All files (*.*)|*.*",
            Multiselect = true,
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        foreach (var file in dialog.FileNames)
        {
            var full = Path.GetFullPath(file);
            if (!Entries.Any(e => !e.IsFolder && string.Equals(e.Path, full, StringComparison.OrdinalIgnoreCase)))
            {
                Entries.Add(new MergeSourceEntry(IsFolder: false, full, Pattern: null));
            }
        }
    }

    [RelayCommand]
    private void AddFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Add a folder to merge (all matching files)", Multiselect = true };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var pattern = string.IsNullOrWhiteSpace(FolderPattern) ? "*.log" : FolderPattern.Trim();
        foreach (var folder in dialog.FolderNames)
        {
            var full = Path.GetFullPath(folder);
            if (!Entries.Any(e => e.IsFolder
                                  && string.Equals(e.Path, full, StringComparison.OrdinalIgnoreCase)
                                  && string.Equals(e.Pattern, pattern, StringComparison.OrdinalIgnoreCase)))
            {
                Entries.Add(new MergeSourceEntry(IsFolder: true, full, pattern));
            }
        }
    }

    [RelayCommand]
    private void Remove(MergeSourceEntry? entry)
    {
        if (entry is not null)
        {
            Entries.Remove(entry);
        }
    }

    /// <summary>Expands every entry to a de-duplicated, order-preserving list of concrete file paths.</summary>
    public IReadOnlyList<string> ResolveFiles()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (var entry in Entries)
        {
            if (entry.IsFolder)
            {
                if (!Directory.Exists(entry.Path))
                {
                    continue;
                }

                foreach (var file in Directory.EnumerateFiles(entry.Path, entry.Pattern ?? "*.log", SearchOption.TopDirectoryOnly)
                             .OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                {
                    var full = Path.GetFullPath(file);
                    if (seen.Add(full))
                    {
                        result.Add(full);
                    }
                }
            }
            else if (seen.Add(entry.Path))
            {
                result.Add(entry.Path);
            }
        }

        return result;
    }

    public bool IsValid => ResolveFiles().Count >= 2;
}
