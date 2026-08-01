using LogViewer.Core.Tailing;

namespace LogViewer.Core.EventLogging;

/// <summary>
/// Mirrors <see cref="ITailSource"/>'s batched-event shape for a Windows Event Log channel, so a
/// document view can host either a file or an event channel polymorphically. Implemented in Phase 2
/// via <c>System.Diagnostics.Eventing.Reader.EventLogWatcher</c>, which subscribes to standard
/// channels (Application, System) without requiring administrator rights.
/// </summary>
public interface IEventLogSource : ITailSource;
