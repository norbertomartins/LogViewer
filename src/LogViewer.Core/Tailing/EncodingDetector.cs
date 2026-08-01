using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace LogViewer.Core.Tailing;

/// <summary>
/// Detects a text file's encoding from a BOM when present, or by sampling the start of the file
/// and checking UTF-8 validity, falling back to the OS ANSI codepage for legacy log files.
/// </summary>
public static class EncodingDetector
{
    private const int SampleSize = 64 * 1024;

#pragma warning disable CA2255 // Intentional: ensures ANSI codepage fallback works for any consumer of this library, including tests.
    [ModuleInitializer]
    internal static void RegisterCodePagesProvider()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }
#pragma warning restore CA2255

    /// <summary>
    /// Detects the encoding of <paramref name="stream"/> without permanently moving its position.
    /// Returns the detected encoding and the length of the BOM preamble to skip, if any.
    /// </summary>
    public static (Encoding Encoding, int PreambleLength) Detect(Stream stream)
    {
        var originalPosition = stream.Position;

        Span<byte> bom = stackalloc byte[4];
        var bomBytesRead = ReadFully(stream, bom);
        stream.Position = originalPosition;

        if (bomBytesRead >= 4 && bom[0] == 0x00 && bom[1] == 0x00 && bom[2] == 0xFE && bom[3] == 0xFF)
        {
            return (new UTF32Encoding(bigEndian: true, byteOrderMark: true), 4);
        }

        if (bomBytesRead >= 4 && bom[0] == 0xFF && bom[1] == 0xFE && bom[2] == 0x00 && bom[3] == 0x00)
        {
            return (new UTF32Encoding(bigEndian: false, byteOrderMark: true), 4);
        }

        if (bomBytesRead >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF)
        {
            return (new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 3);
        }

        if (bomBytesRead >= 2 && bom[0] == 0xFE && bom[1] == 0xFF)
        {
            return (Encoding.BigEndianUnicode, 2);
        }

        if (bomBytesRead >= 2 && bom[0] == 0xFF && bom[1] == 0xFE)
        {
            return (Encoding.Unicode, 2);
        }

        var buffer = new byte[SampleSize];
        var sampleRead = ReadFully(stream, buffer);
        stream.Position = originalPosition;

        var sample = buffer.AsSpan(0, sampleRead);
        var encoding = LooksLikeUtf8(sample)
            ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            : GetAnsiEncoding();

        return (encoding, 0);
    }

    private static int ReadFully(Stream stream, Span<byte> buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer[total..]);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private static bool LooksLikeUtf8(ReadOnlySpan<byte> data)
    {
        var i = 0;
        while (i < data.Length)
        {
            var b = data[i];
            if (b <= 0x7F)
            {
                i++;
                continue;
            }

            var extra = (b & 0xE0) == 0xC0 ? 1
                : (b & 0xF0) == 0xE0 ? 2
                : (b & 0xF8) == 0xF0 ? 3
                : -1;

            if (extra < 0)
            {
                return false;
            }

            if (i + extra >= data.Length)
            {
                // Sequence truncated by the sample boundary — not conclusive either way.
                break;
            }

            for (var j = 1; j <= extra; j++)
            {
                if ((data[i + j] & 0xC0) != 0x80)
                {
                    return false;
                }
            }

            i += extra + 1;
        }

        return true;
    }

    private static Encoding GetAnsiEncoding()
    {
        try
        {
            return Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
        }
        catch (NotSupportedException)
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        }
    }
}
