namespace LogViewer.Core.Configuration;

/// <summary>What kind of source a persisted <see cref="TailSourceSettings"/> entry describes.</summary>
public enum TailSourceKind
{
    File,
    DirectoryWatch,
    EventLog,

    /// <summary>Several files tailed together and interleaved by timestamp (<see cref="TailSourceSettings.MergedPaths"/>).</summary>
    MergedFiles,

    /// <summary>A log endpoint tailed over HTTP(S) — streaming or polled (<see cref="TailSourceSettings.Path"/> holds the URL).</summary>
    RemoteHttp,

    /// <summary>A log stream tailed over a WebSocket (<c>ws://</c> / <c>wss://</c>; <see cref="TailSourceSettings.Path"/> holds the URL).</summary>
    RemoteWebSocket,

    /// <summary>The stdout/stderr of a spawned command (<c>journalctl -f</c>, <c>docker logs -f</c>, …).</summary>
    Process,

    /// <summary>The output of a command run over SSH on a remote host.</summary>
    Ssh,

    /// <summary>A real-time Event Tracing for Windows (ETW) provider.</summary>
    Etw,
}
