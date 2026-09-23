using System.Buffers;
using System.Text;
using LogViewer.Core.Tailing;

namespace LogViewer.Core.Indexing;

/// <summary>One line read back through a <see cref="FileLineIndex"/>. <see cref="LineNumber"/> is 1-based and
/// absolute, matching what <see cref="FileTailSource"/> and the full-text search report for the same file.</summary>
public readonly record struct IndexedLine(long LineNumber, long ByteOffset, string Text);

/// <summary>What an <see cref="FileLineIndex.UpdateAsync"/> call found.</summary>
public enum LineIndexUpdate
{
    Unchanged,
    Grew,

    /// <summary>The file shrank (truncation/rotation) and was re-indexed from scratch.</summary>
    Rebuilt,
}

/// <summary>
/// A sparse line-number → byte-offset index over a text file, so any line of an arbitrarily large file can
/// be read back without loading the file (or even the lines before it) into memory. One checkpoint is kept
/// every <see cref="Stride"/> lines, so memory is O(lines / stride) — a 10 GB file with 100M lines costs ~800 KB
/// of checkpoints at the default stride. Reading line N seeks to the nearest checkpoint at or before it and
/// scans forward at most <see cref="Stride"/> - 1 lines.
/// <para>Indexing is incremental: <see cref="UpdateAsync"/> continues from where the previous call stopped, so a
/// live-growing log only ever scans its new bytes; a file that shrank is re-indexed from the start. Reads and
/// updates may run concurrently (e.g. UI-thread reads while a background update scans the tail).</para>
/// </summary>
public sealed partial class FileLineIndex
{
    public const int DefaultStride = 1024;

    private const int ChunkSize = 256 * 1024;

    private readonly object _sync = new();
    private readonly List<long> _checkpoints = [];
    private readonly SemaphoreSlim _updateGate = new(1, 1);

    private Encoding? _encoding;
    private byte[] _newline = [(byte)'\n'];
    private int _preambleLength;
    private long _indexedOffset;
    private long _completeLineCount;
    private long _fileLength;

    public FileLineIndex(string path, int stride = DefaultStride)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(stride, 1);
        Path = System.IO.Path.GetFullPath(path);
        Stride = stride;
    }

    public string Path { get; }

    public int Stride { get; }

    public Encoding Encoding => _encoding ?? Encoding.UTF8;

    /// <summary>Lines known so far, including a trailing line not yet terminated by a newline.</summary>
    public long LineCount
    {
        get
        {
            lock (_sync)
            {
                return _completeLineCount + (_fileLength > _indexedOffset ? 1 : 0);
            }
        }
    }

    /// <summary>File length (bytes) as of the last <see cref="UpdateAsync"/>.</summary>
    public long FileLength
    {
        get
        {
            lock (_sync)
            {
                return _fileLength;
            }
        }
    }

    /// <summary>
    /// Scans any bytes appended since the previous call (or the whole file on the first call / after it shrank),
    /// reporting progress as a 0..1 fraction of the bytes to scan. Concurrent calls are serialized.
    /// </summary>
    public async Task<LineIndexUpdate> UpdateAsync(IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        await _updateGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => UpdateCore(progress, cancellationToken), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _updateGate.Release();
        }
    }

    private LineIndexUpdate UpdateCore(IProgress<double>? progress, CancellationToken cancellationToken)
    {
        using var stream = OpenShared();
        var length = stream.Length;
        var result = LineIndexUpdate.Grew;

        long indexedOffset;
        lock (_sync)
        {
            if (_encoding is not null && length < _fileLength)
            {
                ResetLocked();
                result = LineIndexUpdate.Rebuilt;
            }

            if (_encoding is null && result != LineIndexUpdate.Rebuilt && TryLoadPersistedLocked(stream))
            {
                // Restored from the on-disk cache; only the bytes after the cached offset get scanned below.
            }

            if (_encoding is null)
            {
                var (encoding, preambleLength) = EncodingDetector.Detect(stream);
                _encoding = encoding;
                _preambleLength = preambleLength;
                _newline = encoding.GetBytes("\n");
                _indexedOffset = preambleLength;
                _checkpoints.Add(preambleLength);
            }

            if (length == _fileLength && result != LineIndexUpdate.Rebuilt)
            {
                return LineIndexUpdate.Unchanged;
            }

            indexedOffset = _indexedOffset;
        }

        var toScan = Math.Max(1, length - indexedOffset);
        var pool = ArrayPool<byte>.Shared;
        var buffer = pool.Rent(ChunkSize);
        try
        {
            stream.Position = indexedOffset;
            var position = indexedOffset;
            var lineCount = CompleteLineCountSnapshot();
            var newCheckpoints = new List<long>();
            var lineEnds = new List<long>();

            while (position < length)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var wanted = (int)Math.Min(ChunkSize, length - position);
                var read = ReadFully(stream, buffer.AsSpan(0, wanted));
                if (read == 0)
                {
                    break;
                }

                var chunk = buffer.AsSpan(0, read);
                var lastLineStart = -1L;
                FindLineEnds(chunk, position, lineEnds);
                foreach (var newlineEnd in lineEnds)
                {
                    lineCount++;
                    lastLineStart = newlineEnd;
                    if (lineCount % Stride == 0)
                    {
                        newCheckpoints.Add(newlineEnd);
                    }
                }

                position += read;

                // Publish progress so readers can use lines as soon as they're indexed, not only at the end.
                lock (_sync)
                {
                    _checkpoints.AddRange(newCheckpoints);
                    newCheckpoints.Clear();
                    _completeLineCount = lineCount;
                    if (lastLineStart >= 0)
                    {
                        _indexedOffset = lastLineStart;
                    }

                    _fileLength = position;
                }

                progress?.Report((double)(position - indexedOffset) / toScan);
            }

            lock (_sync)
            {
                _fileLength = length;
            }

            MaybePersist(length - indexedOffset);
            return result;
        }
        finally
        {
            pool.Return(buffer);
        }
    }

    private long CompleteLineCountSnapshot()
    {
        lock (_sync)
        {
            return _completeLineCount;
        }
    }

    private void ResetLocked()
    {
        _checkpoints.Clear();
        _encoding = null;
        _indexedOffset = 0;
        _completeLineCount = 0;
        _fileLength = 0;
    }

    /// <summary>Yields, for every newline in <paramref name="chunk"/> (which starts at absolute
    /// <paramref name="chunkOffset"/>), the absolute offset of the byte right after it — i.e. the start of the
    /// next line. Multi-byte encodings (UTF-16/32) only match newlines aligned to the code-unit size. Fills the
    /// caller's reusable <paramref name="ends"/> (cleared first) — a fresh list per chunk cost ~25 bytes per line.</summary>
    private void FindLineEnds(ReadOnlySpan<byte> chunk, long chunkOffset, List<long> ends)
    {
        ends.Clear();
        if (_newline.Length == 1)
        {
            var start = 0;
            int index;
            while ((index = chunk[start..].IndexOf(_newline[0])) >= 0)
            {
                start += index + 1;
                ends.Add(chunkOffset + start);
            }

            return;
        }

        var unit = _newline.Length;
        var misalignment = (int)((chunkOffset - _preambleLength) % unit);
        var first = misalignment == 0 ? 0 : unit - misalignment;
        for (var i = first; i + unit <= chunk.Length; i += unit)
        {
            if (chunk.Slice(i, unit).SequenceEqual(_newline))
            {
                ends.Add(chunkOffset + i + unit);
            }
        }
    }

    /// <summary>
    /// Reads up to <paramref name="count"/> lines starting at 1-based <paramref name="firstLineNumber"/>. Returns
    /// fewer (possibly none) when the request runs past the end of the indexed file.
    /// </summary>
    public IReadOnlyList<IndexedLine> ReadLines(long firstLineNumber, int count)
    {
        if (count <= 0 || firstLineNumber < 1)
        {
            return [];
        }

        var result = new List<IndexedLine>(Math.Min(count, 4096));
        ScanLines(firstLineNumber, firstLineNumber + count - 1, line =>
        {
            result.Add(line);
            return true;
        });
        return result;
    }

    /// <summary>
    /// Streams lines <paramref name="firstLineNumber"/>..<paramref name="lastLineNumber"/> (clamped to the indexed
    /// file) to <paramref name="visit"/> in order, stopping early when it returns false. Each line is decoded straight
    /// from the read buffer and handed over at once, so a whole-file pass (count, search) keeps nothing alive between
    /// lines — materializing pages first pushed every decoded string into gen2.
    /// </summary>
    private void ScanLines(long firstLineNumber, long lastLineNumber, Func<IndexedLine, bool> visit)
    {
        long checkpointOffset;
        long checkpointLine;
        long lineCount;
        Encoding encoding;
        lock (_sync)
        {
            if (_encoding is null || _checkpoints.Count == 0)
            {
                return;
            }

            lineCount = _completeLineCount + (_fileLength > _indexedOffset ? 1 : 0);
            if (firstLineNumber > lineCount)
            {
                return;
            }

            var k = (int)Math.Min((firstLineNumber - 1) / Stride, _checkpoints.Count - 1);
            checkpointOffset = _checkpoints[k];
            checkpointLine = (long)k * Stride + 1;
            encoding = _encoding;
        }

        var wantedLast = Math.Min(lineCount, lastLineNumber);

        using var stream = OpenShared();
        stream.Position = checkpointOffset;

        var pool = ArrayPool<byte>.Shared;
        var buffer = pool.Rent(64 * 1024);
        var pending = new ArrayBufferWriter<byte>();
        var lineEnds = new List<long>();
        try
        {
            var lineNumber = checkpointLine;
            var lineStart = checkpointOffset;
            var position = checkpointOffset;
            int read;

            while (lineNumber <= wantedLast && (read = ReadFully(stream, buffer.AsSpan())) > 0)
            {
                var chunk = buffer.AsSpan(0, read);
                var consumed = 0;
                FindLineEnds(chunk, position, lineEnds);
                foreach (var end in lineEnds)
                {
                    var endInChunk = (int)(end - position);
                    if (lineNumber >= firstLineNumber)
                    {
                        string text;
                        if (pending.WrittenCount == 0)
                        {
                            text = Decode(encoding, chunk[consumed..endInChunk], stripNewline: true);
                        }
                        else
                        {
                            pending.Write(chunk[consumed..endInChunk]);
                            text = Decode(encoding, pending.WrittenSpan, stripNewline: true);
                        }

                        if (!visit(new IndexedLine(lineNumber, lineStart, text)))
                        {
                            return;
                        }
                    }

                    pending.Clear();
                    consumed = endInChunk;
                    lineStart = end;
                    lineNumber++;
                    if (lineNumber > wantedLast)
                    {
                        break;
                    }
                }

                if (lineNumber >= firstLineNumber && lineNumber <= wantedLast && consumed < chunk.Length)
                {
                    pending.Write(chunk[consumed..]);
                }

                position += read;
            }

            // Trailing line with no terminating newline.
            if (lineNumber <= wantedLast && lineNumber >= firstLineNumber && pending.WrittenCount > 0)
            {
                visit(new IndexedLine(lineNumber, lineStart, Decode(encoding, pending.WrittenSpan, stripNewline: false)));
            }
        }
        finally
        {
            pool.Return(buffer);
        }
    }

    /// <summary>
    /// Finds the first line whose timestamp (per <paramref name="extractTimestamp"/>) is at or after
    /// <paramref name="target"/>. Binary-searches the checkpoints (sampling the first timestamped line after
    /// each) and then scans forward, so it touches O(log(lines / stride) + stride) lines rather than the whole
    /// file. Assumes timestamps are roughly ascending, which holds for append-only logs. Returns null when no
    /// line at or after <paramref name="target"/> exists.
    /// </summary>
    public long? FindFirstLineAtOrAfter(
        DateTimeOffset target, Func<string, DateTimeOffset?> extractTimestamp, CancellationToken cancellationToken = default)
    {
        const int SampleLines = 64;
        const int PageSize = 512;

        int checkpointCount;
        lock (_sync)
        {
            checkpointCount = _checkpoints.Count;
        }

        if (checkpointCount == 0)
        {
            return null;
        }

        DateTimeOffset? SampleAt(int k)
        {
            foreach (var line in ReadLines((long)k * Stride + 1, SampleLines))
            {
                if (extractTimestamp(line.Text) is { } ts)
                {
                    return ts;
                }
            }

            return null;
        }

        // Largest checkpoint whose sampled timestamp is strictly before the target (or 0).
        int lo = 0, hi = checkpointCount - 1, best = 0;
        while (lo <= hi)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var mid = lo + ((hi - lo) / 2);
            var sample = SampleAt(mid);
            if (sample is null || sample < target)
            {
                best = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        var next = (long)best * Stride + 1;
        var total = LineCount;
        while (next <= total)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var page = ReadLines(next, PageSize);
            if (page.Count == 0)
            {
                break;
            }

            foreach (var line in page)
            {
                if (extractTimestamp(line.Text) is { } ts && ts >= target)
                {
                    return line.LineNumber;
                }
            }

            next = page[^1].LineNumber + 1;
        }

        return null;
    }

    /// <summary>
    /// Scans from the line after (<paramref name="forward"/>) or before <paramref name="fromLine"/> for the first line
    /// satisfying <paramref name="isMatch"/>, reading whole checkpoint-aligned pages so each page costs one seek.
    /// Returns null when the scan reaches the start/end of the file without a match (callers decide whether to wrap).
    /// <paramref name="progress"/> reports the fraction of the remaining range scanned so far.
    /// </summary>
    public IndexedLine? FindNext(
        long fromLine, bool forward, Func<string, bool> isMatch, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var total = LineCount;
        if (total == 0)
        {
            return null;
        }

        if (forward)
        {
            var first = Math.Max(1, fromLine + 1);
            var span = Math.Max(1, total - first + 1);
            IndexedLine? found = null;
            cancellationToken.ThrowIfCancellationRequested();
            ScanLines(first, total, line =>
            {
                if (isMatch(line.Text))
                {
                    found = line;
                    return false;
                }

                if (line.LineNumber % Stride == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    progress?.Report(Math.Min(1, (double)(line.LineNumber - fromLine) / span));
                }

                return true;
            });
            return found;
        }

        var end = Math.Min(fromLine - 1, total); // last line still to check, scanning downwards
        var range = Math.Max(1, end);
        while (end >= 1)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var start = Math.Max(1, end - Stride + 1);
            var page = ReadLines(start, (int)(end - start + 1));
            for (var i = page.Count - 1; i >= 0; i--)
            {
                if (isMatch(page[i].Text))
                {
                    return page[i];
                }
            }

            end = start - 1;
            progress?.Report(Math.Min(1, (double)(range - end) / range));
        }

        return null;
    }

    /// <summary>Counts every line satisfying <paramref name="isMatch"/> in one sequential pass.</summary>
    public long CountMatches(Func<string, bool> isMatch, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        var total = LineCount;
        long count = 0;
        cancellationToken.ThrowIfCancellationRequested();
        ScanLines(1, total, line =>
        {
            if (isMatch(line.Text))
            {
                count++;
            }

            if (line.LineNumber % (Stride * 4) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report((double)line.LineNumber / total);
            }

            return true;
        });
        progress?.Report(1);
        return count;
    }

    private string Decode(Encoding encoding, ReadOnlySpan<byte> bytes, bool stripNewline)
    {
        if (stripNewline && bytes.Length >= _newline.Length)
        {
            bytes = bytes[..^_newline.Length];
        }

        var text = encoding.GetString(bytes);
        return text.Length > 0 && text[^1] == '\r' ? text[..^1] : text;
    }

    private FileStream OpenShared() =>
        new(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 1, FileOptions.SequentialScan);

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
}
