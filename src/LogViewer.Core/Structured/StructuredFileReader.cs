using System.Runtime.CompilerServices;
using LogViewer.Core.Tailing;

namespace LogViewer.Core.Structured;

/// <summary>
/// Streams a file from the start (reusing <see cref="Search.FileFullTextSearchService"/>'s exact
/// streaming approach: <see cref="EncodingDetector"/>/<see cref="LineSplitter"/>, a 64KB read buffer,
/// never materializing the whole file) and yields every line that parses as Serilog JSON, paired with
/// its 1-based line number. Shared by <see cref="BlockDiff.FileBlockScanService"/> and the
/// pattern/property frequency analyzers so they all agree on the same streaming/parsing behavior.
/// </summary>
public static class StructuredFileReader
{
    private const int ReadBufferSize = 64 * 1024;

    public static async IAsyncEnumerable<(long LineNumber, StructuredLogEvent Event)> ReadAsync(
        string path, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var (encoding, preambleLength) = EncodingDetector.Detect(stream);
        stream.Position = preambleLength;

        var splitter = new LineSplitter(encoding);
        var buffer = new byte[ReadBufferSize];
        var lineNumber = 0L;

        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            var lines = splitter.Append(buffer.AsSpan(0, read));
            foreach (var text in lines)
            {
                lineNumber++;
                cancellationToken.ThrowIfCancellationRequested();

                if (SerilogEventParser.TryParse(text, out var evt) && evt is not null)
                {
                    yield return (lineNumber, evt);
                }
            }
        }
    }
}
