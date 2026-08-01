using System.Diagnostics.Eventing.Reader;
using System.Runtime.Versioning;

namespace LogViewer.Core.EventLogging;

/// <summary>Formats an <see cref="EventRecord"/> into the single-line text shown in the tail view, shared
/// between live tailing (<see cref="WindowsEventLogSource"/>) and full-channel search (<see cref="EventLogSearchService"/>)
/// so both surfaces render events identically.</summary>
[SupportedOSPlatform("windows")]
internal static class EventRecordFormatter
{
    public static string? Format(EventRecord record)
    {
        try
        {
            var timestamp = record.TimeCreated?.ToString("yyyy-MM-dd HH:mm:ss") ?? "?";
            var level = SafeGet(() => record.LevelDisplayName) ?? record.Level?.ToString() ?? "Info";
            var provider = record.ProviderName ?? "Unknown";
            var message = SafeGet(record.FormatDescription) ?? "(no description available)";
            return $"{timestamp} [{level}] {provider}: {message}";
        }
        catch (EventLogException)
        {
            return null;
        }
    }

    public static string? SafeGet(Func<string?> getter)
    {
        try
        {
            return getter();
        }
        catch (EventLogException)
        {
            return null;
        }
    }
}
