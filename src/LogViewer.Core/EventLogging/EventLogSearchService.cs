using System.Diagnostics.Eventing.Reader;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using LogViewer.Core.Search;

namespace LogViewer.Core.EventLogging;

/// <summary>
/// Scans an entire EventLog channel (forward, from the oldest retained record) on a background thread
/// via <see cref="EventLogReader"/>, which has no async API, and streams matches back to the caller
/// through an unbounded <see cref="Channel{T}"/> so the UI thread never blocks on the scan.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class EventLogSearchService : IEventLogSearchService
{
    public async IAsyncEnumerable<SearchResult> SearchAsync(
        string channelName,
        IReadOnlyList<EventLogFilterRule> filters,
        string pattern,
        bool isRegex,
        bool isCaseSensitive,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            yield break;
        }

        var channel = Channel.CreateUnbounded<SearchResult>();
        var producer = Task.Run(
            () => Produce(channel.Writer, channelName, filters, pattern, isRegex, isCaseSensitive, cancellationToken),
            cancellationToken);

        await foreach (var result in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return result;
        }

        await producer.ConfigureAwait(false);
    }

    private static void Produce(
        ChannelWriter<SearchResult> writer,
        string channelName,
        IReadOnlyList<EventLogFilterRule> filters,
        string pattern,
        bool isRegex,
        bool isCaseSensitive,
        CancellationToken cancellationToken)
    {
        Exception? failure = null;
        try
        {
            // Deliberately not RegexOptions.Compiled — see the comment in FileFullTextSearchService.SearchAsync:
            // this regex is built fresh per search and matched in one pass, so Compiled's one-time JIT cost
            // (benchmarked at ~4.3ms) outweighs its faster per-call matching except on very large channel scans.
            var regex = isRegex
                ? new Regex(pattern, isCaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase)
                : null;
            var comparison = isCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

            var filterEvaluator = new EventLogFilterEvaluator();
            filterEvaluator.SetFilters(filters);

            using var reader = new EventLogReader(new EventLogQuery(channelName, PathType.LogName));
            var lineNumber = 0L;

            while (!cancellationToken.IsCancellationRequested)
            {
                using var record = reader.ReadEvent();
                if (record is null)
                {
                    break;
                }

                var formatted = EventRecordFormatter.Format(record);
                if (formatted is null || !filterEvaluator.PassesFilters(record, formatted))
                {
                    continue;
                }

                lineNumber++;
                if (IsMatch(formatted, regex, pattern, comparison))
                {
                    writer.TryWrite(new SearchResult(lineNumber, 0, formatted));
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Cooperative cancellation — not a failure.
        }
        catch (Exception ex)
        {
            failure = ex;
        }
        finally
        {
            writer.Complete(failure);
        }
    }

    private static bool IsMatch(string text, Regex? regex, string pattern, StringComparison comparison)
        => regex is not null ? regex.IsMatch(text) : text.Contains(pattern, comparison);
}
