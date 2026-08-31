using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace LogViewer.Core.Tailing;

/// <summary>
/// Transparent read access to gzip-compressed log files (<c>.gz</c>). A compressed archive can't be
/// incrementally tailed, so the file is decompressed once into a stable per-source temp file that the
/// normal <see cref="FileTailSource"/>/search/structured pipeline then opens unchanged.
/// </summary>
public static class CompressedLogFile
{
    private static readonly byte[] GzipMagic = [0x1F, 0x8B];

    /// <summary>True when <paramref name="path"/> begins with the gzip magic bytes (extension-independent).</summary>
    public static bool IsGzip(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            Span<byte> header = stackalloc byte[2];
            return stream.ReadAtLeast(header, 2, throwOnEndOfStream: false) == 2
                && header[0] == GzipMagic[0] && header[1] == GzipMagic[1];
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// If <paramref name="path"/> is gzip-compressed, decompresses it into a temp file and returns that
    /// path; otherwise returns <paramref name="path"/> unchanged. The temp file is reused (not rewritten)
    /// while the source's path, size and last-write time are unchanged, so reopening is cheap.
    /// </summary>
    public static string Materialize(string path)
    {
        if (!IsGzip(path))
        {
            return path;
        }

        var info = new FileInfo(path);
        var stamp = $"{path}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(stamp)))[..16];

        var dir = Path.Combine(Path.GetTempPath(), "LogViewer", "gz");
        Directory.CreateDirectory(dir);

        var name = Path.GetFileNameWithoutExtension(path);
        if (Path.GetExtension(name).Length == 0)
        {
            name += ".log";
        }

        var target = Path.Combine(dir, $"{name}.{hash}{Path.GetExtension(name)}");
        if (File.Exists(target) && new FileInfo(target).Length > 0)
        {
            return target;
        }

        var tmp = target + ".partial";
        using (var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var gzip = new GZipStream(source, CompressionMode.Decompress))
        using (var output = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            gzip.CopyTo(output);
        }

        File.Move(tmp, target, overwrite: true);
        return target;
    }
}
