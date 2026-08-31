using System.IO.Compression;
using System.Text;
using LogViewer.Core.Tailing;

namespace LogViewer.Core.Tests.Tailing;

public sealed class CompressedLogFileTests
{
    [Fact]
    public void IsGzip_TrueForGzipContent_FalseForPlainText()
    {
        var plain = Path.GetTempFileName();
        var gz = Path.GetTempFileName();
        try
        {
            File.WriteAllText(plain, "hello world\n");
            WriteGzip(gz, "hello world\n");

            Assert.False(CompressedLogFile.IsGzip(plain));
            Assert.True(CompressedLogFile.IsGzip(gz));
        }
        finally
        {
            File.Delete(plain);
            File.Delete(gz);
        }
    }

    [Fact]
    public void Materialize_PlainFile_ReturnedUnchanged()
    {
        var plain = Path.GetTempFileName();
        try
        {
            File.WriteAllText(plain, "line\n");
            Assert.Equal(plain, CompressedLogFile.Materialize(plain));
        }
        finally
        {
            File.Delete(plain);
        }
    }

    [Fact]
    public void Materialize_GzipFile_DecompressesAndCachesByStamp()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"cfl-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var gz = Path.Combine(dir, "app.log.gz");
        try
        {
            const string content = "2026-01-01 INFO up\n2026-01-01 ERROR down\n";
            WriteGzip(gz, content);

            var first = CompressedLogFile.Materialize(gz);
            Assert.NotEqual(gz, first);
            Assert.Equal(content, File.ReadAllText(first));

            var secondWriteTime = File.GetLastWriteTimeUtc(first);
            var second = CompressedLogFile.Materialize(gz);
            Assert.Equal(first, second);
            Assert.Equal(secondWriteTime, File.GetLastWriteTimeUtc(second)); // reused, not rewritten
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static void WriteGzip(string path, string text)
    {
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var gzip = new GZipStream(file, CompressionLevel.Optimal);
        gzip.Write(Encoding.UTF8.GetBytes(text));
    }
}
