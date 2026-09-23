using System.IO.Compression;
using System.Text;
using LogViewer.Core.Tailing;
using LogViewer.Core.Tests.TestUtilities;
using SharpCompress.Compressors.BZip2;
using SharpCompress.Compressors.ZStandard;

namespace LogViewer.Core.Tests.Tailing;

public sealed class ArchiveFormatsTests
{
    private const string Content = "2026-09-23 10:00:00 INFO one\n2026-09-23 10:00:01 ERROR two\n";

    [Fact]
    public void BZip2_IsDetectedAndDecompressed()
    {
        using var file = new TempFileFixture("app.log.bz2");
        using (var output = File.Create(file.FilePath))
        using (var bz = BZip2Stream.Create(output, SharpCompress.Compressors.CompressionMode.Compress, false))
        {
            bz.Write(Encoding.UTF8.GetBytes(Content));
        }

        Assert.Equal(CompressionKind.BZip2, CompressedLogFile.Detect(file.FilePath));
        var plain = CompressedLogFile.Materialize(file.FilePath);
        Assert.EndsWith(".log", plain);
        Assert.Equal(Content, File.ReadAllText(plain));
    }

    [Fact]
    public void Zstandard_IsDetectedAndDecompressed()
    {
        using var file = new TempFileFixture("app.log.zst");
        using (var output = File.Create(file.FilePath))
        using (var zstd = new CompressionStream(output))
        {
            zstd.Write(Encoding.UTF8.GetBytes(Content));
        }

        Assert.Equal(CompressionKind.Zstandard, CompressedLogFile.Detect(file.FilePath));
        Assert.Equal(Content, File.ReadAllText(CompressedLogFile.Materialize(file.FilePath)));
    }

    [Fact]
    public void Zip_ListsEntriesLargestFirst_AndMaterializesTheChosenOne()
    {
        using var file = new TempFileFixture("logs.zip");
        using (var archive = ZipFile.Open(file.FilePath, ZipArchiveMode.Create))
        {
            Write(archive, "small.log", "tiny\n");
            Write(archive, "nested/big.log", Content + Content);
            archive.CreateEntry("empty-dir/");
        }

        Assert.Equal(CompressionKind.Zip, CompressedLogFile.Detect(file.FilePath));
        Assert.Equal(["nested/big.log", "small.log"], CompressedLogFile.ListZipEntries(file.FilePath).Select(e => e.FullName));

        Assert.Equal(Content + Content, File.ReadAllText(CompressedLogFile.Materialize(file.FilePath)));
        var small = CompressedLogFile.Materialize(file.FilePath, "small.log");
        Assert.Equal("tiny\n", File.ReadAllText(small));
        Assert.StartsWith("small.", Path.GetFileName(small));
    }

    [Fact]
    public void PlainText_IsNotCompressed()
    {
        using var file = new TempFileFixture();
        file.WriteAllText(Content);
        Assert.Equal(CompressionKind.None, CompressedLogFile.Detect(file.FilePath));
        Assert.Equal(file.FilePath, CompressedLogFile.Materialize(file.FilePath));
    }

    private static void Write(ZipArchive archive, string name, string text)
    {
        using var stream = archive.CreateEntry(name).Open();
        stream.Write(Encoding.UTF8.GetBytes(text));
    }
}
