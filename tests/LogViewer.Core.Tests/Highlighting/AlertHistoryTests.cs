using LogViewer.Core.Highlighting;

namespace LogViewer.Core.Tests.Highlighting;

public sealed class AlertHistoryTests
{
    [Fact]
    public void Record_KeepsTheMostRecentEntries_OldestFirst()
    {
        var history = new AlertHistory(capacity: 3);
        for (var i = 1; i <= 5; i++)
        {
            history.Record(new AlertRecord(DateTimeOffset.UnixEpoch.AddSeconds(i), AlertKind.Threshold, "r", i, $"line {i}"));
        }

        Assert.Equal([3L, 4, 5], history.Snapshot().Select(r => r.LineNumber));
    }
}
