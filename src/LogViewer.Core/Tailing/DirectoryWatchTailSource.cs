namespace LogViewer.Core.Tailing;

/// <summary>
/// Watches a directory for files matching a wildcard pattern. When <paramref name="autoSwitchEnabled"/>
/// is true, tailing automatically switches to whichever matching file was most recently modified —
/// this composes a single inner <see cref="FileTailSource"/> at a time and re-points it as the latest
/// match changes, forwarding its events under this source's identity so consumers never see a
/// difference between tailing one fixed file and tailing "whatever's newest in this directory".
///
/// <para>Resilient to the watched directory itself disappearing and later reappearing (e.g. a deploy
/// step that recreates a logs folder from scratch): a <see cref="FileSystemWatcher"/> rooted at a
/// specific directory refers to that directory's underlying handle, not the path string, so once the
/// directory is deleted the watcher's handle is dead and recreating the directory at the same path does
/// not bring it back to life — a fresh <see cref="FileSystemWatcher"/> has to be constructed. A poll
/// timer (<see cref="DirectoryPollInterval"/>) is the backstop that detects both directions: it drops
/// the watcher once <c>Directory.Exists</c> goes false, and recreates it (then rescans for matches) once
/// the directory exists again — independent of whether the OS happens to raise the watcher's
/// <see cref="FileSystemWatcher.Error"/> event for the deletion, which isn't guaranteed.</para>
/// </summary>
public sealed class DirectoryWatchTailSource : ITailSource
{
    private static readonly TimeSpan DefaultDirectoryPollInterval = TimeSpan.FromSeconds(1);

    private readonly string _directoryPath;
    private readonly string _wildcardPattern;
    private readonly bool _autoSwitchEnabled;
    private readonly TailSourceOptions _fileOptions;
    private readonly TimeSpan _directoryPollInterval;
    private readonly object _sync = new();

    private FileSystemWatcher? _watcher;
    private Timer? _directoryPollTimer;
    private FileTailSource? _activeFileSource;
    private string? _activeFilePath;
    private bool _started;
    private bool _hasEverAttached;

    /// <param name="directoryPollInterval">How often to check for the watched directory having disappeared
    /// or reappeared. Defaults to 1 second — this is a resilience backstop, not the hot per-line tailing
    /// path, so it doesn't need to be as tight as <paramref name="fileOptions"/>'s poll interval. Exposed
    /// mainly so tests don't have to wait a full second per assertion.</param>
    public DirectoryWatchTailSource(string directoryPath, string wildcardPattern, bool autoSwitchEnabled, TailSourceOptions? fileOptions = null, TimeSpan? directoryPollInterval = null)
    {
        _directoryPath = Path.GetFullPath(directoryPath);
        _wildcardPattern = wildcardPattern;
        _autoSwitchEnabled = autoSwitchEnabled;
        _fileOptions = fileOptions ?? new TailSourceOptions();
        _directoryPollInterval = directoryPollInterval ?? DefaultDirectoryPollInterval;
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

            TryAttachWatcher();

            _directoryPollTimer = new Timer(_ => SafeDirectoryPollCheck(), null, _directoryPollInterval, _directoryPollInterval);
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
            _directoryPollTimer?.Dispose();
            _directoryPollTimer = null;
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
                SwitchToLatestMatch(bypassAutoSwitchGuard: false);
            }
        }
    }

    /// <summary>Creates the <see cref="FileSystemWatcher"/> and does an initial scan for matches — but only
    /// when the directory actually exists, since the constructor throws otherwise. Called with <see cref="_sync"/>
    /// already held, both from <see cref="Start"/> and from the poll timer once a previously-missing directory
    /// reappears.</summary>
    private void TryAttachWatcher()
    {
        if (_watcher is not null || !Directory.Exists(_directoryPath))
        {
            return;
        }

        try
        {
            _watcher = new FileSystemWatcher(_directoryPath, _wildcardPattern)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
            };
            _watcher.Created += (_, _) => SafeSwitchCheck();
            _watcher.Changed += (_, _) => SafeSwitchCheck();
            _watcher.Renamed += (_, _) => SafeSwitchCheck();
            _watcher.Error += (_, e) => OnWatcherError(e.GetException());
            _watcher.EnableRaisingEvents = true;
        }
        catch (ArgumentException)
        {
            // The directory vanished again between the Directory.Exists check and here — the next poll
            // tick will retry.
            _watcher = null;
            return;
        }

        // Whatever was tailed under the previous watcher (if any) belonged to a directory instance that
        // no longer exists — drop it so the scan below always reattaches, even if the recreated directory
        // happens to contain a same-named file, and bypass the auto-switch gate: this is (re)establishing
        // the initial file to tail, not switching away from one that's already flowing.
        DetachFileSource();
        SwitchToLatestMatch(bypassAutoSwitchGuard: true);
    }

    /// <summary>Fires on the watcher's own thread when its underlying handle breaks — most commonly because
    /// the directory it's rooted at was deleted. Drops the dead watcher immediately; the poll timer picks a
    /// new one up once the directory exists again.</summary>
    private void OnWatcherError(Exception ex)
    {
        Error?.Invoke(this, new TailSourceErrorEventArgs(ex));

        lock (_sync)
        {
            if (!_started)
            {
                return;
            }

            _watcher?.Dispose();
            _watcher = null;
        }
    }

    /// <summary>Backstop for directory disappearance/reappearance that doesn't rely on the OS reliably
    /// raising <see cref="FileSystemWatcher.Error"/> — see the type-level remarks.</summary>
    private void SafeDirectoryPollCheck()
    {
        lock (_sync)
        {
            if (!_started)
            {
                return;
            }

            if (_watcher is null)
            {
                TryAttachWatcher();
                return;
            }

            if (!Directory.Exists(_directoryPath))
            {
                _watcher.Dispose();
                _watcher = null;
            }
        }
    }

    /// <summary><paramref name="bypassAutoSwitchGuard"/> is true when this call is (re)establishing the
    /// first file to tail — either the very first one ever, or the first one after the directory itself
    /// came back from being deleted — as opposed to switching away from a file that's already flowing,
    /// which respects <see cref="_autoSwitchEnabled"/>.</summary>
    private void SwitchToLatestMatch(bool bypassAutoSwitchGuard)
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

        if (!bypassAutoSwitchGuard && !_autoSwitchEnabled)
        {
            return;
        }

        // Reset must be raised before the new file's content starts flowing — consumers clear their
        // display on reset, so firing it after Start() would wipe out the lines Start() just delivered.
        // Skipped only for the very first file this instance ever attaches to, when there's nothing on
        // screen yet to clear — including recovering from the directory having been deleted and recreated,
        // which should visibly reset the view even though it's technically this instance's first *active*
        // file after the gap.
        AttachFileSource(latestPath, raiseReset: _hasEverAttached);
        _hasEverAttached = true;
    }

    private void AttachFileSource(string path, bool raiseReset)
    {
        DetachFileSource();

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
