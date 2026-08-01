using System.Text;

namespace LogViewer.Core.Tailing;

/// <summary>
/// Incrementally decodes a byte stream into lines, carrying a small pending-partial-line buffer
/// across calls so a line (or a multi-byte character) split across two reads is handled correctly.
/// </summary>
public sealed class LineSplitter
{
    private readonly Decoder _decoder;
    private readonly StringBuilder _pending = new();
    private char[] _charBuffer = new char[4096];

    public LineSplitter(Encoding encoding)
    {
        _decoder = encoding.GetDecoder();
    }

    /// <summary>
    /// Decodes <paramref name="bytes"/> and returns any newly completed lines. Text after the last
    /// newline is retained internally and prefixed to the next call's output instead of being dropped.
    /// </summary>
    public List<string> Append(ReadOnlySpan<byte> bytes)
    {
        var lines = new List<string>();
        if (bytes.IsEmpty)
        {
            return lines;
        }

        var maxCharCount = _decoder.GetCharCount(bytes, flush: false);
        if (_charBuffer.Length < maxCharCount)
        {
            _charBuffer = new char[maxCharCount];
        }

        var charCount = _decoder.GetChars(bytes, _charBuffer.AsSpan(0, maxCharCount), flush: false);
        var span = _charBuffer.AsSpan(0, charCount);

        var start = 0;
        for (var i = 0; i < span.Length; i++)
        {
            if (span[i] != '\n')
            {
                continue;
            }

            var segment = span[start..i];
            if (!segment.IsEmpty && segment[^1] == '\r')
            {
                segment = segment[..^1];
            }

            lines.Add(_pending.Length == 0 ? segment.ToString() : _pending.Append(segment).ToString());
            _pending.Clear();
            start = i + 1;
        }

        if (start < span.Length)
        {
            _pending.Append(span[start..]);
        }

        return lines;
    }

    /// <summary>Discards any buffered partial line and decoder state, e.g. after a truncation/rotation reset.</summary>
    public void Reset()
    {
        _pending.Clear();
        _decoder.Reset();
    }
}
