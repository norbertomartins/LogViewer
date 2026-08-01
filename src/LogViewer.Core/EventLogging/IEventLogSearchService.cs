using LogViewer.Core.Search;

namespace LogViewer.Core.EventLogging;

/// <summary>
/// Searches an entire EventLog channel (not just the buffered/visible tail), applying the same
/// filter rules as the live <see cref="WindowsEventLogSource"/> tailing that channel.
/// </summary>
public interface IEventLogSearchService
{
    IAsyncEnumerable<SearchResult> SearchAsync(
        string channelName,
        IReadOnlyList<EventLogFilterRule> filters,
        string pattern,
        bool isRegex,
        bool isCaseSensitive,
        CancellationToken cancellationToken);
}
