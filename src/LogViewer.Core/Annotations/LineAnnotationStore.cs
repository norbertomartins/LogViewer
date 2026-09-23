using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LogViewer.Core.Annotations;

/// <summary>A user note on one line of a file. <paramref name="TextHash"/> is <see cref="LineAnnotationStore.HashText"/>
/// of the line when the note was written: a note is only shown while the line at that number still has that text, so
/// a rotated or rewritten file doesn't show old notes on unrelated lines.</summary>
public sealed record LineAnnotation(long LineNumber, string TextHash, string Note, DateTimeOffset CreatedAt);

public interface ILineAnnotationStore
{
    /// <summary>The notes stored for <paramref name="fileKey"/> (a document's path or session key), by line number.</summary>
    IReadOnlyList<LineAnnotation> Get(string fileKey);

    /// <summary>Replaces the notes for <paramref name="fileKey"/>; an empty list removes the file's entry.</summary>
    void Set(string fileKey, IReadOnlyList<LineAnnotation> annotations);
}

/// <summary>
/// Line notes persisted to their own JSON file (<c>%LOCALAPPDATA%\LogViewer\annotations.json</c>) rather than
/// settings.json, so a heavily annotated investigation doesn't grow the settings file or need a schema bump. Keyed by
/// the case-insensitive file key; the whole file is rewritten on every change (notes are few and small). Any I/O or
/// parse error reads as "no notes" and a failed write is dropped — notes are a convenience, never worth a crash.
/// </summary>
public sealed class LineAnnotationStore : ILineAnnotationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly object _sync = new();
    private Dictionary<string, List<LineAnnotation>>? _entries;

    public LineAnnotationStore(string path) => _path = path;

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LogViewer", "annotations.json");

    /// <summary>Stable across processes (unlike <see cref="string.GetHashCode()"/>): first 16 hex chars of SHA-256.</summary>
    public static string HashText(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16];

    public IReadOnlyList<LineAnnotation> Get(string fileKey)
    {
        lock (_sync)
        {
            return Load().TryGetValue(fileKey, out var list) ? [.. list] : [];
        }
    }

    public void Set(string fileKey, IReadOnlyList<LineAnnotation> annotations)
    {
        lock (_sync)
        {
            var entries = Load();
            if (annotations.Count == 0)
            {
                entries.Remove(fileKey);
            }
            else
            {
                entries[fileKey] = [.. annotations.OrderBy(a => a.LineNumber)];
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var temp = _path + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(entries, JsonOptions));
                File.Move(temp, _path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Kept in memory for this session; the next successful write persists it.
            }
        }
    }

    private Dictionary<string, List<LineAnnotation>> Load()
    {
        if (_entries is not null)
        {
            return _entries;
        }

        Dictionary<string, List<LineAnnotation>>? loaded = null;
        try
        {
            if (File.Exists(_path))
            {
                loaded = JsonSerializer.Deserialize<Dictionary<string, List<LineAnnotation>>>(File.ReadAllText(_path), JsonOptions);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            loaded = null;
        }

        _entries = new Dictionary<string, List<LineAnnotation>>(loaded ?? [], StringComparer.OrdinalIgnoreCase);
        return _entries;
    }
}
