using System.Text;
using LogViewer.Core.Tailing;

namespace LogViewer.Core.Tests.Tailing;

public sealed class EncodingDetectorTests
{
    [Fact]
    public void Detect_Utf8Bom_ReturnsUtf8AndSkipsPreamble()
    {
        using var stream = new MemoryStream([0xEF, 0xBB, 0xBF, (byte)'h', (byte)'i']);

        var (encoding, preambleLength) = EncodingDetector.Detect(stream);

        Assert.Equal(Encoding.UTF8.CodePage, encoding.CodePage);
        Assert.Equal(3, preambleLength);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void Detect_Utf16LeBom_ReturnsUnicode()
    {
        using var stream = new MemoryStream([0xFF, 0xFE, (byte)'h', 0x00]);

        var (encoding, preambleLength) = EncodingDetector.Detect(stream);

        Assert.Equal(Encoding.Unicode.CodePage, encoding.CodePage);
        Assert.Equal(2, preambleLength);
    }

    [Fact]
    public void Detect_Utf16BeBom_ReturnsBigEndianUnicode()
    {
        using var stream = new MemoryStream([0xFE, 0xFF, 0x00, (byte)'h']);

        var (encoding, preambleLength) = EncodingDetector.Detect(stream);

        Assert.Equal(Encoding.BigEndianUnicode.CodePage, encoding.CodePage);
        Assert.Equal(2, preambleLength);
    }

    [Fact]
    public void Detect_NoBomValidUtf8Content_ReturnsUtf8()
    {
        var bytes = Encoding.UTF8.GetBytes("plain ascii and unicode: café");
        using var stream = new MemoryStream(bytes);

        var (encoding, preambleLength) = EncodingDetector.Detect(stream);

        Assert.Equal(Encoding.UTF8.CodePage, encoding.CodePage);
        Assert.Equal(0, preambleLength);
    }

    [Fact]
    public void Detect_NoBomInvalidUtf8Content_FallsBackToAnsi()
    {
        // 0xFF/0xFE bytes with no valid UTF-8 continuation pattern.
        byte[] bytes = [0x41, 0x42, 0xFF, 0xFE, 0x90, 0x00, 0x43];
        using var stream = new MemoryStream(bytes);

        var (encoding, preambleLength) = EncodingDetector.Detect(stream);

        Assert.NotEqual(Encoding.UTF8.CodePage, encoding.CodePage);
        Assert.Equal(0, preambleLength);
    }

    [Fact]
    public void Detect_DoesNotConsumeStreamPosition()
    {
        var bytes = Encoding.UTF8.GetBytes("some content");
        using var stream = new MemoryStream(bytes);
        stream.Position = 0;

        EncodingDetector.Detect(stream);

        Assert.Equal(0, stream.Position);
    }
}
