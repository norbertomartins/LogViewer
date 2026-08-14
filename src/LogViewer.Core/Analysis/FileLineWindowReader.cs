using LogViewer.Core.Tailing;

namespace LogViewer.Core.Analysis;

/// <summary>Streams a file from the start once (reusing the same <see cref="EncodingDetector"/>/
/// <see cref="LineSplitter"/> pattern as the rest of Core) and collects only the lines inside the
/// requested window, exiting early once past it — bounded and cheap even on huge files.</summary>
public sealed class FileLineWindowReader : ILineWindowReader
{
    private const int ReadBufferSize = 64 * 1024;

    public async Task<LineWindowResult> ReadAsync(
        string sourcePath, long centerLineNumber, int linesBefore, int linesAfter, CancellationToken cancellationToken)
    {
        if (centerLineNumber < 1)
        {
            return new LineWindowResult(centerLineNumber, true, []);
        }

        var start = Math.Max(1, centerLineNumber - linesBefore);
        var end = centerLineNumber + linesAfter;

        var collected = new List<ContextLine>();
        var maxLineSeen = 0L;

        await using var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
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

                if (lineNumber >= start && lineNumber <= end)
                {
                    collected.Add(new ContextLine(lineNumber, text));
                }
            }

            maxLineSeen = lineNumber;
            if (lineNumber >= end)
            {
                break;
            }
        }

        var outOfRange = centerLineNumber > maxLineSeen;
        return new LineWindowResult(centerLineNumber, outOfRange, collected);
    }
}
