using System.Buffers;
using System.Text;

namespace LogViewer.Core.Tailing;

/// <summary>
/// Tails a single text file: opens showing only its tail, then appends newly written bytes as they
/// arrive, detecting truncation/rotation/deletion via <see cref="FileChangeDetector"/>. Never reads
/// the whole file into memory — only <c>[lastOffset, currentLength)</c> on each read cycle.
/// </summary>
public sealed class FileTailSource : ITailSource
{
    private readonly string _path;
    private readonly TailSourceOptions _options;
    private readonly FileChangeDetector _changeDetector = new();
    private readonly object _sync = new();

    private FileSystemWatcher? _watcher;
    private Timer? _pollTimer;
    private LineSplitter? _splitter;
    private Encoding? _encoding;
    private long _readOffset;
    private long _lineNumber;
    private bool _started;

    public FileTailSource(string path, TailSourceOptions? options = null)
    {
        _path = Path.GetFullPath(path);
        _options = options ?? new TailSourceOptions();
        DisplayName = _path;
    }

    public string DisplayName { get; }

    public event EventHandler<TailLinesReadEventArgs>? LinesRead;
    public event EventHandler<TailSourceResetEventArgs>? SourceReset;
    public event EventHandler<TailSourceErrorEventArgs>? Error;

    public void Start()
    {
        lock (_sync)
        {
            if (_started)
            {
                return;
            }

            _started = true;

            var directory = Path.GetDirectoryName(_path)!;
            _watcher = new FileSystemWatcher(directory, Path.GetFileName(_path))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
            };
            _watcher.Changed += OnWatcherEvent;
            _watcher.Created += OnWatcherEvent;
            _watcher.Renamed += OnWatcherEvent;
            _watcher.Deleted += OnWatcherEvent;
            _watcher.Error += (_, e) => Error?.Invoke(this, new TailSourceErrorEventArgs(e.GetException()));
            _watcher.EnableRaisingEvents = true;

            _pollTimer = new Timer(_ => SafeCheck(), null, _options.PollInterval, _options.PollInterval);

            SafeCheck();
        }
    }

    public void Stop()
    {
        lock (_sync)
        {
            if (!_started)
            {
                return;
            }

            _started = false;
            _watcher?.Dispose();
            _watcher = null;
            _pollTimer?.Dispose();
            _pollTimer = null;
        }
    }

    public void Dispose() => Stop();

    private void OnWatcherEvent(object sender, FileSystemEventArgs e) => SafeCheck();

    private void SafeCheck()
    {
        if (!Monitor.TryEnter(_sync))
        {
            return;
        }

        try
        {
            if (!_started)
            {
                return;
            }

            CheckForChanges();
        }
        catch (IOException)
        {
            // Transient — the file may be mid-write or mid-rotation by another process; the next tick retries.
        }
        catch (Exception ex)
        {
            Error?.Invoke(this, new TailSourceErrorEventArgs(ex));
        }
        finally
        {
            Monitor.Exit(_sync);
        }
    }

    private void CheckForChanges()
    {
        var kind = _changeDetector.Check(_path);

        switch (kind)
        {
            case FileChangeKind.Deleted:
                if (_splitter is not null)
                {
                    ResetState();
                    SourceReset?.Invoke(this, new TailSourceResetEventArgs(TailResetReason.Deleted));
                }

                return;

            case FileChangeKind.Rotated:
                ResetState();
                OpenAndRead(TailResetReason.Rotated);
                return;

            case FileChangeKind.Truncated:
                ResetState();
                OpenAndRead(TailResetReason.Truncated);
                return;

            default:
                OpenAndRead(resetReason: null);
                return;
        }
    }

    private void ResetState()
    {
        _splitter = null;
        _encoding = null;
        _readOffset = 0;
        _lineNumber = 0;
    }

    private void OpenAndRead(TailResetReason? resetReason)
    {
        using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        if (_splitter is null)
        {
            var (encoding, preambleLength) = _options.EncodingOverride is { } overrideEncoding
                ? (overrideEncoding, 0)
                : EncodingDetector.Detect(stream);
            _encoding = encoding;
            _splitter = new LineSplitter(encoding);

            // On a fresh open we show only the tail; on a reset (truncate/rotate) there's nothing
            // meaningful before the new content, so we start from right after the preamble.
            _readOffset = resetReason is null
                ? ComputeInitialOffset(stream, preambleLength)
                : preambleLength;

            if (resetReason is { } reason)
            {
                SourceReset?.Invoke(this, new TailSourceResetEventArgs(reason));
            }
        }

        if (stream.Length > _readOffset)
        {
            ReadNewData(stream);
        }
    }

    private void ReadNewData(FileStream stream)
    {
        stream.Position = _readOffset;
        var pool = ArrayPool<byte>.Shared;
        var buffer = pool.Rent(_options.ReadBufferSize);
        try
        {
            List<TailLine>? newLines = null;
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                var chunkOffset = _readOffset;
                var decodedLines = _splitter!.Append(buffer.AsSpan(0, read));
                foreach (var text in decodedLines)
                {
                    _lineNumber++;
                    (newLines ??= new List<TailLine>()).Add(new TailLine(_lineNumber, chunkOffset, text, DateTimeOffset.UtcNow));
                }

                _readOffset += read;
            }

            if (newLines is { Count: > 0 })
            {
                LinesRead?.Invoke(this, new TailLinesReadEventArgs(newLines));
            }
        }
        finally
        {
            pool.Return(buffer);
        }
    }

    private long ComputeInitialOffset(FileStream stream, int preambleLength)
    {
        var wanted = _options.InitialTailLineCount;
        var length = stream.Length;
        if (wanted <= 0 || length <= preambleLength)
        {
            return preambleLength;
        }

        var unitSize = _encoding switch
        {
            UTF32Encoding => 4,
            UnicodeEncoding => 2,
            _ => 1,
        };

        const int chunkSize = 64 * 1024;
        var buffer = new byte[chunkSize];
        var position = length;
        var newlineCount = 0;
        long? foundOffset = null;

        while (position > preambleLength)
        {
            var readSize = (int)Math.Min(chunkSize, position - preambleLength);
            position -= readSize;
            stream.Position = position;
            var read = ReadFully(stream, buffer.AsSpan(0, readSize));

            for (var i = read - 1; i >= 0; i--)
            {
                if (buffer[i] != (byte)'\n')
                {
                    continue;
                }

                newlineCount++;
                if (newlineCount > wanted)
                {
                    foundOffset = position + i + 1;
                    break;
                }
            }

            if (foundOffset is not null)
            {
                break;
            }
        }

        var offset = foundOffset ?? preambleLength;

        // Align to the encoding's code-unit size so the resumed read never starts mid-character.
        var relative = offset - preambleLength;
        relative -= relative % unitSize;
        return preambleLength + relative;
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
}
