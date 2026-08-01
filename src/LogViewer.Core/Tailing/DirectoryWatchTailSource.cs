namespace LogViewer.Core.Tailing;

/// <summary>
/// Watches a directory for files matching a wildcard pattern. When <paramref name="autoSwitchEnabled"/>
/// is true, tailing automatically switches to whichever matching file was most recently modified —
/// this composes a single inner <see cref="FileTailSource"/> at a time and re-points it as the latest
/// match changes, forwarding its events under this source's identity so consumers never see a
/// difference between tailing one fixed file and tailing "whatever's newest in this directory".
/// </summary>
public sealed class DirectoryWatchTailSource : ITailSource
{
    private readonly string _directoryPath;
    private readonly string _wildcardPattern;
    private readonly bool _autoSwitchEnabled;
    private readonly TailSourceOptions _fileOptions;
    private readonly object _sync = new();

    private FileSystemWatcher? _watcher;
    private FileTailSource? _activeFileSource;
    private string? _activeFilePath;
    private bool _started;

    public DirectoryWatchTailSource(string directoryPath, string wildcardPattern, bool autoSwitchEnabled, TailSourceOptions? fileOptions = null)
    {
        _directoryPath = Path.GetFullPath(directoryPath);
        _wildcardPattern = wildcardPattern;
        _autoSwitchEnabled = autoSwitchEnabled;
        _fileOptions = fileOptions ?? new TailSourceOptions();
        DisplayName = Path.Combine(_directoryPath, wildcardPattern);
    }

    public string DisplayName { get; }

    /// <summary>The currently-tailed file's full path, or null before the first match is found.</summary>
    public string? ActiveFilePath => _activeFilePath;

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

            SwitchToLatestMatch(isInitialAttach: true);

            _watcher = new FileSystemWatcher(_directoryPath, _wildcardPattern)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
            };
            _watcher.Created += (_, _) => SafeSwitchCheck();
            _watcher.Changed += (_, _) => SafeSwitchCheck();
            _watcher.Renamed += (_, _) => SafeSwitchCheck();
            _watcher.Error += (_, e) => Error?.Invoke(this, new TailSourceErrorEventArgs(e.GetException()));
            _watcher.EnableRaisingEvents = true;
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
            DetachFileSource();
        }
    }

    public void Dispose() => Stop();

    private void SafeSwitchCheck()
    {
        if (!_autoSwitchEnabled)
        {
            return;
        }

        lock (_sync)
        {
            if (_started)
            {
                SwitchToLatestMatch(isInitialAttach: false);
            }
        }
    }

    private void SwitchToLatestMatch(bool isInitialAttach)
    {
        string? latestPath;
        try
        {
            latestPath = Directory.EnumerateFiles(_directoryPath, _wildcardPattern)
                .Select(p => new FileInfo(p))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault()
                ?.FullName;
        }
        catch (IOException ex)
        {
            Error?.Invoke(this, new TailSourceErrorEventArgs(ex));
            return;
        }
        catch (UnauthorizedAccessException ex)
        {
            Error?.Invoke(this, new TailSourceErrorEventArgs(ex));
            return;
        }

        if (latestPath is null)
        {
            return;
        }

        if (string.Equals(latestPath, _activeFilePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!isInitialAttach && !_autoSwitchEnabled)
        {
            return;
        }

        AttachFileSource(latestPath, raiseReset: !isInitialAttach);
    }

    private void AttachFileSource(string path, bool raiseReset)
    {
        DetachFileSource();

        // Reset must be raised before the new file's content starts flowing — consumers clear their
        // display on reset, so firing it after Start() would wipe out the lines Start() just delivered.
        if (raiseReset)
        {
            SourceReset?.Invoke(this, new TailSourceResetEventArgs(TailResetReason.Rotated));
        }

        _activeFilePath = path;
        var fileSource = new FileTailSource(path, _fileOptions);
        fileSource.LinesRead += (_, e) => LinesRead?.Invoke(this, e);
        fileSource.SourceReset += (_, e) => SourceReset?.Invoke(this, e);
        fileSource.Error += (_, e) => Error?.Invoke(this, e);
        _activeFileSource = fileSource;
        fileSource.Start();
    }

    private void DetachFileSource()
    {
        _activeFileSource?.Dispose();
        _activeFileSource = null;
        _activeFilePath = null;
    }
}
