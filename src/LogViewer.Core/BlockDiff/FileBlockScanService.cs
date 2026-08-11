using System.Runtime.CompilerServices;
using LogViewer.Core.Structured;
using LogViewer.Core.Tailing;

namespace LogViewer.Core.BlockDiff;

/// <summary>
/// Streams a target file from the start (reusing <see cref="Search.FileFullTextSearchService"/>'s
/// exact streaming approach: <see cref="EncodingDetector"/>/<see cref="LineSplitter"/>, a 64KB read
/// buffer, never materializing the whole file) and segments its structured lines into <see cref="LogBlock"/>s.
/// </summary>
public sealed class FileBlockScanService : IBlockScanService
{
    private const int ReadBufferSize = 64 * 1024;
    private const int CorrelationSweepInterval = 1000;

    public async IAsyncEnumerable<LogBlock> ScanAsync(
        string targetPath,
        BlockDetectionOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var events = ReadStructuredEventsAsync(targetPath, cancellationToken);

        var blocks = options.Strategy == BlockDetectionStrategy.ByCorrelationField
            ? ScanByCorrelation(events, targetPath, options, cancellationToken)
            : ScanByProximity(events, targetPath, options, cancellationToken);

        await foreach (var block in blocks.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (block.Lines.Count > 0)
            {
                yield return block;
            }
        }
    }

    private static async IAsyncEnumerable<(long LineNumber, StructuredLogEvent Event)> ReadStructuredEventsAsync(
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

    /// <summary>Single active cluster per ThreadId (or a shared key for events without one) — O(1) memory,
    /// safe for arbitrarily large files. A cluster is finalized/yielded and a new one started whenever the
    /// gap since that thread's last event exceeds <see cref="BlockDetectionOptions.ProximityMaxGap"/>, or once
    /// it reaches <see cref="BlockDetectionOptions.ProximityMaxLines"/>.</summary>
    private static async IAsyncEnumerable<LogBlock> ScanByProximity(
        IAsyncEnumerable<(long LineNumber, StructuredLogEvent Event)> events,
        string sourceDescription,
        BlockDetectionOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        const string noThreadKey = "\0__no-thread__";

        var clusters = new Dictionary<string, List<LogBlockLine>>();
        var lastTimestamp = new Dictionary<string, DateTimeOffset?>();

        await foreach (var (lineNumber, evt) in events.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            var threadId = StructuredFieldResolver.Resolve(evt, "ThreadId") ?? noThreadKey;
            var line = new LogBlockLine(lineNumber, MessageSignature.Compute(evt), evt);

            if (clusters.TryGetValue(threadId, out var existing))
            {
                var gapOk = lastTimestamp[threadId] is not { } prevTs || evt.Timestamp is not { } curTs
                    || (curTs - prevTs).Duration() <= options.ProximityMaxGap;

                if (!gapOk || existing.Count >= options.ProximityMaxLines)
                {
                    yield return new LogBlock(existing, null, null, sourceDescription);
                    existing = [];
                    clusters[threadId] = existing;
                }
            }
            else
            {
                existing = [];
                clusters[threadId] = existing;
            }

            existing.Add(line);
            lastTimestamp[threadId] = evt.Timestamp;
        }

        foreach (var cluster in clusters.Values)
        {
            if (cluster.Count > 0)
            {
                yield return new LogBlock(cluster, null, null, sourceDescription);
            }
        }
    }

    /// <summary>Groups by the resolved value of <see cref="BlockDetectionOptions.CorrelationField"/>. Since the
    /// same correlation value can't be pre-filtered (its concrete value differs run-to-run/version-to-version),
    /// this must track every distinct value seen; periodically sweeps and finalizes groups quiet for more than
    /// <see cref="BlockDetectionOptions.QuietLineGap"/> lines, and LRU-evicts once <see cref="BlockDetectionOptions.MaxTrackedGroups"/>
    /// is exceeded, to keep memory bounded on files with huge numbers of distinct correlation ids.</summary>
    private static async IAsyncEnumerable<LogBlock> ScanByCorrelation(
        IAsyncEnumerable<(long LineNumber, StructuredLogEvent Event)> events,
        string sourceDescription,
        BlockDetectionOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var field = options.CorrelationField
            ?? throw new ArgumentException("CorrelationField is required for the ByCorrelationField strategy.", nameof(options));

        var groups = new Dictionary<string, List<LogBlockLine>>(StringComparer.Ordinal);
        var lastSeenLine = new Dictionary<string, long>(StringComparer.Ordinal);
        var lruOrder = new LinkedList<string>();
        var lruNodeByKey = new Dictionary<string, LinkedListNode<string>>(StringComparer.Ordinal);

        var linesSinceSweep = 0;

        await foreach (var (lineNumber, evt) in events.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            var value = StructuredFieldResolver.Resolve(evt, field);
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            if (!groups.TryGetValue(value, out var lines))
            {
                lines = [];
                groups[value] = lines;
            }

            lines.Add(new LogBlockLine(lineNumber, MessageSignature.Compute(evt), evt));
            lastSeenLine[value] = lineNumber;
            TouchLru(lruOrder, lruNodeByKey, value);

            linesSinceSweep++;
            if (linesSinceSweep >= CorrelationSweepInterval)
            {
                linesSinceSweep = 0;
                foreach (var finalized in SweepQuietGroups(groups, lastSeenLine, lruOrder, lruNodeByKey, lineNumber, options.QuietLineGap, field, sourceDescription))
                {
                    yield return finalized;
                }
            }

            while (groups.Count > options.MaxTrackedGroups && lruOrder.First is not null)
            {
                var evictKey = lruOrder.First.Value;
                lruOrder.RemoveFirst();
                lruNodeByKey.Remove(evictKey);
                if (groups.Remove(evictKey, out var evictedLines))
                {
                    lastSeenLine.Remove(evictKey);
                    yield return new LogBlock(evictedLines, field, evictKey, sourceDescription);
                }
            }
        }

        foreach (var kvp in groups)
        {
            yield return new LogBlock(kvp.Value, field, kvp.Key, sourceDescription);
        }
    }

    private static void TouchLru(LinkedList<string> order, Dictionary<string, LinkedListNode<string>> nodeByKey, string key)
    {
        if (nodeByKey.TryGetValue(key, out var node))
        {
            order.Remove(node);
        }

        nodeByKey[key] = order.AddLast(key);
    }

    private static List<LogBlock> SweepQuietGroups(
        Dictionary<string, List<LogBlockLine>> groups,
        Dictionary<string, long> lastSeenLine,
        LinkedList<string> lruOrder,
        Dictionary<string, LinkedListNode<string>> lruNodeByKey,
        long currentLineNumber,
        int quietLineGap,
        string correlationField,
        string sourceDescription)
    {
        var finalized = new List<LogBlock>();
        var staleKeys = groups.Keys.Where(k => currentLineNumber - lastSeenLine[k] > quietLineGap).ToList();

        foreach (var key in staleKeys)
        {
            finalized.Add(new LogBlock(groups[key], correlationField, key, sourceDescription));
            groups.Remove(key);
            lastSeenLine.Remove(key);
            if (lruNodeByKey.Remove(key, out var node))
            {
                lruOrder.Remove(node);
            }
        }

        return finalized;
    }
}
