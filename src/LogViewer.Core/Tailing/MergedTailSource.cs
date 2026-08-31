namespace LogViewer.Core.Tailing;

/// <summary>
/// Tails several text files at once and presents them as a single stream ordered by each line's
/// timestamp. Each emitted line is prefixed with a short per-file label (<c>label│ original text</c>).
/// <para>Live merging can't globally sort an unbounded stream, so lines pass through a bounded
/// <b>reorder buffer</b>: a line is held for <c>reorderWindow</c> (default 2s) after it arrives before
/// being flushed, and the buffered-and-due lines are sorted by timestamp on each flush. A line whose
/// earlier-timestamped sibling from another file arrives more than a window late will still appear
/// slightly out of order — the window trades a small display latency for near-correct ordering.</para>
/// </summary>
public sealed class MergedTailSource : ITailSource
{
    private sealed record Pending(DateTimeOffset SortKey, DateTimeOffset EnqueuedUtc, long Seq, string Label, string Text);

    private readonly List<(string Label, FileTailSource Source)> _members = [];
    private readonly Func<string, DateTimeOffset?> _timestampExtractor;
    private readonly TimeSpan _reorderWindow;
    private readonly Func<DateTimeOffset> _clock;
    private readonly object _sync = new();
    private readonly List<Pending> _pending = [];
    private readonly Dictionary<string, DateTimeOffset> _lastSeenByLabel = new(StringComparer.Ordinal);

    private System.Threading.Timer? _flushTimer;
    private long _seq;
    private long _emittedLineNumber;
    private bool _started;
    private bool _disposed;

    public MergedTailSource(
        IReadOnlyList<string> paths,
        TailSourceOptions? options = null,
        Func<string, DateTimeOffset?>? timestampExtractor = null,
        TimeSpan? reorderWindow = null,
        Func<DateTimeOffset>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Count < 2)
        {
            throw new ArgumentException("A merged source needs at least two files.", nameof(paths));
        }

        _timestampExtractor = timestampExtractor ?? MergedTimestampExtractor.TryExtract;
        _reorderWindow = reorderWindow ?? TimeSpan.FromSeconds(2);
        _clock = clock ?? (() => DateTimeOffset.UtcNow);

        foreach (var (path, label) in AssignLabels(paths))
        {
            var source = new FileTailSource(path, options);
            source.LinesRead += (_, e) => OnChildLines(label, e.Lines);
            source.Error += (_, e) => Error?.Invoke(this, e);
            source.SourceReset += (_, e) => SourceReset?.Invoke(this, e);
            _members.Add((label, source));
        }

        DisplayName = BuildDisplayName(paths);
    }

    public string DisplayName { get; }

    public event EventHandler<TailLinesReadEventArgs>? LinesRead;
    public event EventHandler<TailSourceResetEventArgs>? SourceReset;
    public event EventHandler<TailSourceErrorEventArgs>? Error;

    /// <summary>The per-file labels, in the order given (for a legend).</summary>
    public IReadOnlyList<string> Labels => _members.Select(m => m.Label).ToList();

    public void Start()
    {
        lock (_sync)
        {
            if (_started || _disposed)
            {
                return;
            }

            _started = true;
            var period = TimeSpan.FromMilliseconds(Math.Max(100, _reorderWindow.TotalMilliseconds / 2));
            _flushTimer = new System.Threading.Timer(_ => FlushDueLines(), null, period, period);
        }

        foreach (var (_, source) in _members)
        {
            source.Start();
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            _flushTimer?.Dispose();
            _flushTimer = null;
            _started = false;
        }

        foreach (var (_, source) in _members)
        {
            source.Stop();
        }

        FlushAll();
    }

    public void Dispose()
    {
        Stop();

        lock (_sync)
        {
            _disposed = true;
        }

        foreach (var (_, source) in _members)
        {
            source.Dispose();
        }
    }

    private void OnChildLines(string label, IReadOnlyList<TailLine> lines)
    {
        lock (_sync)
        {
            var now = _clock();
            foreach (var line in lines)
            {
                DateTimeOffset sortKey;
                if (_timestampExtractor(line.Text) is { } ts)
                {
                    sortKey = ts;
                    _lastSeenByLabel[label] = ts;
                }
                else
                {
                    // Carry forward the file's last known timestamp so a continuation line (stack trace,
                    // wrapped message) stays next to the line it belongs with.
                    sortKey = _lastSeenByLabel.TryGetValue(label, out var last) ? last : DateTimeOffset.MinValue;
                }

                _pending.Add(new Pending(sortKey, now, _seq++, label, line.Text));
            }
        }
    }

    private void FlushDueLines() => FlushDueAt(_clock());

    /// <summary>Flushes lines that have been buffered for at least one reorder window as of
    /// <paramref name="now"/>. Internal for testing the reorder behavior deterministically.</summary>
    internal void FlushDueAt(DateTimeOffset now) => FlushOlderThan(now - _reorderWindow);

    /// <summary>Flushes every buffered line enqueued at or before <paramref name="cutoff"/>, sorted by
    /// timestamp then arrival order.</summary>
    private void FlushOlderThan(DateTimeOffset cutoff)
    {
        List<Pending> due;
        lock (_sync)
        {
            if (_pending.Count == 0)
            {
                return;
            }

            due = _pending.Where(p => p.EnqueuedUtc <= cutoff).ToList();
            if (due.Count == 0)
            {
                return;
            }

            _pending.RemoveAll(p => p.EnqueuedUtc <= cutoff);
        }

        Emit(due);
    }

    private void FlushAll()
    {
        List<Pending> due;
        lock (_sync)
        {
            if (_pending.Count == 0)
            {
                return;
            }

            due = [.. _pending];
            _pending.Clear();
        }

        Emit(due);
    }

    private void Emit(List<Pending> due)
    {
        due.Sort(static (a, b) =>
        {
            var byKey = a.SortKey.CompareTo(b.SortKey);
            return byKey != 0 ? byKey : a.Seq.CompareTo(b.Seq);
        });

        var stamp = _clock();
        var batch = new List<TailLine>(due.Count);
        foreach (var p in due)
        {
            batch.Add(new TailLine(Interlocked.Increment(ref _emittedLineNumber), 0, $"{p.Label}│ {p.Text}", stamp));
        }

        LinesRead?.Invoke(this, new TailLinesReadEventArgs(batch));
    }

    // --- Labeling / naming ---------------------------------------------------------------------------

    internal static IEnumerable<(string Path, string Label)> AssignLabels(IReadOnlyList<string> paths)
    {
        var used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            var baseLabel = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrEmpty(baseLabel))
            {
                baseLabel = Path.GetFileName(path);
            }

            if (string.IsNullOrEmpty(baseLabel))
            {
                baseLabel = "file";
            }

            if (used.TryGetValue(baseLabel, out var count))
            {
                used[baseLabel] = count + 1;
                yield return (path, $"{baseLabel}#{count + 1}");
            }
            else
            {
                used[baseLabel] = 1;
                yield return (path, baseLabel);
            }
        }
    }

    private static string BuildDisplayName(IReadOnlyList<string> paths)
    {
        var names = paths.Select(Path.GetFileName).ToList();
        return names.Count <= 3
            ? $"Merged: {string.Join(", ", names)}"
            : $"Merged: {string.Join(", ", names.Take(2))} (+{names.Count - 2})";
    }
}
