using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using LogViewer.Core.Structured;
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
        string? propertyName,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            yield break;
        }

        // Deliberately not RegexOptions.Compiled: this regex is built fresh per search and used for one
        // streaming pass, never reused across searches. Benchmarked (see benchmarks/LogViewer.Benchmarks/
        // FullTextSearchRegexBenchmarks.cs): Compiled's one-time JIT cost (~4.3ms) made a 1,000-line search
        // ~29x slower overall, and only broke even somewhere past ~40k matched lines — a regression for the
        // common case. Compiled pays off only when the same Regex instance is matched many times, which is
        // the pattern EventLogFilterEvaluator's per-rule cache exploits, not this one-shot usage.
        Regex? regex = isRegex
            ? new Regex(pattern, isCaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase)
            : null;
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

                if (IsMatch(text, pattern, regex, comparison, propertyName))
                {
                    yield return new SearchResult(lineNumber, chunkOffset, text);
                }
            }

            offset += read;
        }
    }

    private static bool IsMatch(string text, string pattern, Regex? regex, StringComparison comparison, string? propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
        {
            return regex is not null ? regex.IsMatch(text) : text.Contains(pattern, comparison);
        }

        if (!SerilogEventParser.TryParse(text, out var evt))
        {
            return false;
        }

        var candidate = StructuredFieldResolver.Resolve(evt, propertyName);
        if (candidate is null)
        {
            return false;
        }

        return regex is not null ? regex.IsMatch(candidate) : candidate.Contains(pattern, comparison);
    }
}
