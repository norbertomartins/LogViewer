using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using SharpCompress.Compressors.BZip2;
using SharpCompress.Compressors.ZStandard;

namespace LogViewer.Core.Tailing;

/// <summary>Compression container detected from a file's leading magic bytes (never from its extension).</summary>
public enum CompressionKind
{
    None,
    Gzip,
    Zip,
    BZip2,
    Zstandard,
}

/// <summary>One file inside a <c>.zip</c> archive.</summary>
public sealed record ArchiveEntry(string FullName, long Length);

/// <summary>
/// Transparent read access to compressed log files — gzip (<c>.gz</c>), bzip2 (<c>.bz2</c>), Zstandard (<c>.zst</c>)
/// and zip (<c>.zip</c>, one entry at a time). A compressed file can't be incrementally tailed, so it is decompressed
/// once into a stable temp file that the normal <see cref="FileTailSource"/>/search/structured pipeline then opens
/// unchanged. The temp copy is reused (not rewritten) while the source's path, size and last-write time are unchanged.
/// </summary>
public static class CompressedLogFile
{
    public static CompressionKind Detect(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            Span<byte> header = stackalloc byte[4];
            var read = stream.ReadAtLeast(header, 4, throwOnEndOfStream: false);
            return header[..read] switch
            {
                [0x1F, 0x8B, ..] => CompressionKind.Gzip,
                [0x50, 0x4B, 0x03, 0x04] => CompressionKind.Zip,
                [0x42, 0x5A, 0x68, ..] => CompressionKind.BZip2, // "BZh"
                [0x28, 0xB5, 0x2F, 0xFD] => CompressionKind.Zstandard,
                _ => CompressionKind.None,
            };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return CompressionKind.None;
        }
    }

    /// <summary>True when <paramref name="path"/> begins with the gzip magic bytes (extension-independent).</summary>
    public static bool IsGzip(string path) => Detect(path) == CompressionKind.Gzip;

    /// <summary>The file entries of a zip archive (directories skipped), largest first.</summary>
    public static IReadOnlyList<ArchiveEntry> ListZipEntries(string path)
    {
        using var archive = ZipFile.OpenRead(path);
        return [.. archive.Entries
            .Where(e => !e.FullName.EndsWith('/') && e.Length > 0)
            .Select(e => new ArchiveEntry(e.FullName, e.Length))
            .OrderByDescending(e => e.Length)];
    }

    /// <summary>
    /// If <paramref name="path"/> is compressed, decompresses it into a temp file and returns that path; otherwise
    /// returns <paramref name="path"/> unchanged. For a zip, <paramref name="zipEntry"/> picks the entry (default: the
    /// largest one).
    /// </summary>
    public static string Materialize(string path, string? zipEntry = null)
    {
        var kind = Detect(path);
        if (kind == CompressionKind.None)
        {
            return path;
        }

        if (kind == CompressionKind.Zip)
        {
            zipEntry ??= ListZipEntries(path).FirstOrDefault()?.FullName
                ?? throw new InvalidDataException("The zip archive contains no files.");
        }

        var info = new FileInfo(path);
        var stamp = $"{path}|{zipEntry}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(stamp)))[..16];

        var dir = Path.Combine(Path.GetTempPath(), "LogViewer", "decompressed");
        Directory.CreateDirectory(dir);

        var name = kind == CompressionKind.Zip ? Path.GetFileName(zipEntry!) : Path.GetFileNameWithoutExtension(path);
        if (Path.GetExtension(name).Length == 0)
        {
            name += ".log";
        }

        var target = Path.Combine(dir, $"{Path.GetFileNameWithoutExtension(name)}.{hash}{Path.GetExtension(name)}");
        if (File.Exists(target) && new FileInfo(target).Length > 0)
        {
            return target;
        }

        var tmp = target + ".partial";
        using (var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var output = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            if (kind == CompressionKind.Zip)
            {
                using var archive = new ZipArchive(source, ZipArchiveMode.Read);
                var entry = archive.GetEntry(zipEntry!) ?? throw new InvalidDataException($"No entry '{zipEntry}' in the archive.");
                using var entryStream = entry.Open();
                entryStream.CopyTo(output);
            }
            else
            {
                using var decompressed = OpenDecompressor(kind, source);
                decompressed.CopyTo(output);
            }
        }

        File.Move(tmp, target, overwrite: true);
        return target;
    }

    private static Stream OpenDecompressor(CompressionKind kind, Stream source) => kind switch
    {
        CompressionKind.Gzip => new GZipStream(source, CompressionMode.Decompress),
        CompressionKind.BZip2 => BZip2Stream.Create(source, SharpCompress.Compressors.CompressionMode.Decompress, true),
        CompressionKind.Zstandard => new DecompressionStream(source),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
