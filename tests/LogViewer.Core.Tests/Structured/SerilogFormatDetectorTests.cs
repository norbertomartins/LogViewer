using LogViewer.Core.Structured;
using LogViewer.Core.Tests.TestUtilities;

namespace LogViewer.Core.Tests.Structured;

public sealed class SerilogFormatDetectorTests
{
    [Fact]
    public void LooksLikeSerilogJson_AllLinesClef_ReturnsTrue()
    {
        var lines = new[]
        {
            @"{""@t"":""2026-01-01T00:00:00Z"",""@mt"":""started"",""@l"":""Information""}",
            @"{""@t"":""2026-01-01T00:00:01Z"",""@mt"":""processing {Id}"",""Id"":1}",
            @"{""@t"":""2026-01-01T00:00:02Z"",""@mt"":""done""}",
        };

        Assert.True(SerilogFormatDetector.LooksLikeSerilogJson(lines));
    }

    [Fact]
    public void LooksLikeSerilogJson_PlainTextLines_ReturnsFalse()
    {
        var lines = new[] { "2026-01-01 info: started", "2026-01-01 warn: retrying", "2026-01-01 info: done" };

        Assert.False(SerilogFormatDetector.LooksLikeSerilogJson(lines));
    }

    [Fact]
    public void LooksLikeSerilogJson_BelowMinimumSampleSize_ReturnsFalse()
    {
        var lines = new[] { @"{""@t"":""2026-01-01T00:00:00Z"",""@mt"":""started""}" };

        Assert.False(SerilogFormatDetector.LooksLikeSerilogJson(lines, minSamples: 3));
    }

    [Fact]
    public void LooksLikeSerilogJson_MixedBelowThreshold_ReturnsFalse()
    {
        var lines = new[]
        {
            @"{""@t"":""2026-01-01T00:00:00Z"",""@mt"":""started""}",
            "plain text line one",
            "plain text line two",
            "plain text line three",
        };

        Assert.False(SerilogFormatDetector.LooksLikeSerilogJson(lines, threshold: 0.8));
    }

    [Fact]
    public void SniffFile_ClefFile_ReturnsTrue()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText(string.Join('\n',
        [
            @"{""@t"":""2026-01-01T00:00:00Z"",""@mt"":""started"",""@l"":""Information""}",
            @"{""@t"":""2026-01-01T00:00:01Z"",""@mt"":""processing {Id}"",""Id"":1}",
            @"{""@t"":""2026-01-01T00:00:02Z"",""@mt"":""done""}",
            "",
        ]));

        Assert.True(SerilogFormatDetector.SniffFile(fixture.FilePath));
    }

    [Fact]
    public void SniffFile_PlainTextFile_ReturnsFalse()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText("info: starting\nwarn: retrying\ninfo: done\n");

        Assert.False(SerilogFormatDetector.SniffFile(fixture.FilePath));
    }

    [Fact]
    public void SniffFile_MissingFile_ReturnsFalse()
    {
        Assert.False(SerilogFormatDetector.SniffFile(@"C:\this\path\does\not\exist.log"));
    }
}
