using System.Security.Cryptography;
using System.Text;

namespace LogViewer.Core.Indexing;

/// <summary>
/// On-disk cache for <see cref="FileLineIndex"/>, so a multi-GB file that is reopened (browser, MCP time-range
/// query, next app session) only scans the bytes appended since it was last indexed instead of the whole file.
/// <para>A cache entry is trusted only when the file still starts with the same bytes (hash of the first 4 KB),
/// still contains the same bytes just before the last indexed offset (hash of that 4 KB), and hasn't shrunk —
/// i.e. it is the same file, only appended to. Anything else (rotation, truncation, rewrite) discards the entry and
/// the file is indexed from scratch. Only files of at least <see cref="PersistMinFileSize"/> are persisted, at most
/// <see cref="MaxPersistedFiles"/> entries are kept (oldest removed), and any I/O error just means "no cache".</para>
/// </summary>
public sealed partial class FileLineIndex
{
    private const string Magic = "LVIDX1";
    private const int HashWindow = 4096;

    /// <summary>Folder for persisted indexes; null (the default) disables persistence. The app points it at
    /// <c>%LOCALAPPDATA%\LogViewer\index-cache</c>.</summary>
    public static string? PersistDirectory { get; set; }

    /// <summary>Smaller files re-index fast enough that caching them isn't worth the disk writes.</summary>
    public static long PersistMinFileSize { get; set; } = 64L * 1024 * 1024;

    public const int MaxPersistedFiles = 32;

    private long _bytesScannedSincePersist;

    /// <summary>True when this index started from a persisted cache entry instead of a full scan.</summary>
    public bool LoadedFromCache { get; private set; }

    private string? CacheFilePath()
    {
        if (PersistDirectory is not { } directory)
        {
            return null;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Path.ToUpperInvariant()));
        return System.IO.Path.Combine(directory, Convert.ToHexString(hash)[..32] + ".lvidx");
    }

    /// <summary>Called under <c>_sync</c> on a fresh index. Restores state from the cache entry when it still
    /// describes this file; returns false (leaving the index empty) otherwise.</summary>
    private bool TryLoadPersistedLocked(FileStream stream)
    {
        var cachePath = CacheFilePath();
        if (cachePath is null || !File.Exists(cachePath))
        {
            return false;
        }

        try
        {
            using var reader = new BinaryReader(File.OpenRead(cachePath), Encoding.UTF8);
            if (reader.ReadString() != Magic
                || !string.Equals(reader.ReadString(), Path, StringComparison.OrdinalIgnoreCase)
                || reader.ReadInt32() != Stride)
            {
                return false;
            }

            var codePage = reader.ReadInt32();
            var preambleLength = reader.ReadInt32();
            var fileLength = reader.ReadInt64();
            var indexedOffset = reader.ReadInt64();
            var completeLineCount = reader.ReadInt64();
            var headHash = reader.ReadBytes(32);
            var tailHash = reader.ReadBytes(32);
            var count = reader.ReadInt32();
            if (stream.Length < fileLength || indexedOffset > fileLength || count < 1
                || !headHash.AsSpan().SequenceEqual(HashRange(stream, 0, Math.Min(HashWindow, fileLength)))
                || !tailHash.AsSpan().SequenceEqual(HashRange(stream, Math.Max(0, indexedOffset - HashWindow), indexedOffset)))
            {
                return false;
            }

            var checkpoints = new long[count];
            for (var i = 0; i < count; i++)
            {
                checkpoints[i] = reader.ReadInt64();
            }

            var encoding = Encoding.GetEncoding(codePage);
            _encoding = encoding;
            _preambleLength = preambleLength;
            _newline = encoding.GetBytes("\n");
            _checkpoints.AddRange(checkpoints);
            _indexedOffset = indexedOffset;
            _completeLineCount = completeLineCount;

            // Resume from the last complete line: the partial tail (if any) is re-scanned with whatever was appended.
            _fileLength = indexedOffset;
            LoadedFromCache = true;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EndOfStreamException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    /// <summary>Writes the cache entry after an update that scanned a meaningful amount of a large file.</summary>
    private void MaybePersist(long bytesScanned)
    {
        var cachePath = CacheFilePath();
        if (cachePath is null)
        {
            return;
        }

        _bytesScannedSincePersist += bytesScanned;
        long fileLength, indexedOffset, completeLineCount;
        int preambleLength, codePage;
        long[] checkpoints;
        lock (_sync)
        {
            if (_encoding is null || _fileLength < PersistMinFileSize || _bytesScannedSincePersist < Math.Max(1, PersistMinFileSize / 4))
            {
                return;
            }

            fileLength = _fileLength;
            indexedOffset = _indexedOffset;
            completeLineCount = _completeLineCount;
            preambleLength = _preambleLength;
            codePage = _encoding.CodePage;
            checkpoints = [.. _checkpoints];
        }

        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(cachePath)!);
            byte[] headHash, tailHash;
            using (var stream = OpenShared())
            {
                headHash = HashRange(stream, 0, Math.Min(HashWindow, fileLength));
                tailHash = HashRange(stream, Math.Max(0, indexedOffset - HashWindow), indexedOffset);
            }

            var temp = cachePath + ".tmp";
            using (var writer = new BinaryWriter(File.Create(temp), Encoding.UTF8))
            {
                writer.Write(Magic);
                writer.Write(Path);
                writer.Write(Stride);
                writer.Write(codePage);
                writer.Write(preambleLength);
                writer.Write(fileLength);
                writer.Write(indexedOffset);
                writer.Write(completeLineCount);
                writer.Write(headHash);
                writer.Write(tailHash);
                writer.Write(checkpoints.Length);
                foreach (var checkpoint in checkpoints)
                {
                    writer.Write(checkpoint);
                }
            }

            File.Move(temp, cachePath, overwrite: true);
            _bytesScannedSincePersist = 0;
            PruneCache(System.IO.Path.GetDirectoryName(cachePath)!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort — a missing cache only costs a re-scan next time.
        }
    }

    private static void PruneCache(string directory)
    {
        var entries = new DirectoryInfo(directory).GetFiles("*.lvidx").OrderByDescending(f => f.LastWriteTimeUtc).ToList();
        foreach (var stale in entries.Skip(MaxPersistedFiles))
        {
            stale.Delete();
        }
    }

    private static byte[] HashRange(FileStream stream, long start, long end)
    {
        var length = (int)Math.Max(0, end - start);
        var buffer = new byte[length];
        stream.Position = start;
        var read = ReadFully(stream, buffer);
        return SHA256.HashData(buffer.AsSpan(0, read));
    }
}
