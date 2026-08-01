using System.Text;

namespace LogViewer.Core.Tailing;

/// <summary>Configures how a <see cref="FileTailSource"/> opens and reads a file.</summary>
public sealed class TailSourceOptions
{
    /// <summary>Number of lines to read backwards from EOF when the source is first opened.</summary>
    public int InitialTailLineCount { get; init; } = 1000;

    /// <summary>Fallback poll interval used alongside <see cref="System.IO.FileSystemWatcher"/>, since the
    /// watcher is known to miss or coalesce events under heavy writers or on network shares.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Explicit encoding override. When null, the encoding is auto-detected on open.</summary>
    public Encoding? EncodingOverride { get; init; }

    /// <summary>Size of the pooled read buffer, in bytes.</summary>
    public int ReadBufferSize { get; init; } = 64 * 1024;
}
