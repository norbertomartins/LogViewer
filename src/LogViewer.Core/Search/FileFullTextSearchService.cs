using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using LogViewer.Core.Tailing;

namespace LogViewer.Core.Search;

/// <summary>
/// Streams a text file from the start and yields every matching line, independent of the bounded
/// tailing ring buffer — so it can find matches that were evicted from (or never yet reached) the
/// live view. Reuses <see cref="EncodingDetector"/>/<see cref="LineSplitter"/> from the tailing engine
/// and never materializes the whole file in memory.
/// </summary>
public sealed class FileFullTextSearchService : IFullTextSearchService
{
    private const int ReadBufferSize = 64 * 1024;

    public async IAsyncEnumerable<SearchResult> SearchAsync(
        string sourcePath,
        string pattern,
        bool isRegex,
        bool isCaseSensitive,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            yield break;
        }

        Regex? regex = isRegex ? new Regex(pattern, isCaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase) : null;
        var comparison = isCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        await using var stream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var (encoding, preambleLength) = EncodingDetector.Detect(stream);
        stream.Position = preambleLength;

        var splitter = new LineSplitter(encoding);
        var buffer = new byte[ReadBufferSize];
        var lineNumber = 0L;
        var offset = (long)preambleLength;

        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            var chunkOffset = offset;
            var lines = splitter.Append(buffer.AsSpan(0, read));
            foreach (var text in lines)
            {
                lineNumber++;
                cancellationToken.ThrowIfCancellationRequested();

                if (IsMatch(text, pattern, regex, comparison))
                {
                    yield return new SearchResult(lineNumber, chunkOffset, text);
                }
            }

            offset += read;
        }
    }

    private static bool IsMatch(string text, string pattern, Regex? regex, StringComparison comparison)
        => regex is not null ? regex.IsMatch(text) : text.Contains(pattern, comparison);
}
