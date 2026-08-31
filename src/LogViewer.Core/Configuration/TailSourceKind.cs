namespace LogViewer.Core.Configuration;

/// <summary>What kind of source a persisted <see cref="TailSourceSettings"/> entry describes.</summary>
public enum TailSourceKind
{
    File,
    DirectoryWatch,
    EventLog,

    /// <summary>Several files tailed together and interleaved by timestamp (<see cref="TailSourceSettings.MergedPaths"/>).</summary>
    MergedFiles,
}
