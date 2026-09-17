using LogViewer.Core.Highlighting;

namespace LogViewer.Core.Tests.Highlighting;

public sealed class AlertWindowTrackerTests
{
    [Fact]
    public void RecordHit_BelowThreshold_ReturnsFalse()
    {
        var tracker = new AlertWindowTracker();
        var ruleId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        Assert.False(tracker.RecordHit(ruleId, now, thresholdCount: 3, TimeSpan.FromSeconds(60)));
        Assert.False(tracker.RecordHit(ruleId, now.AddSeconds(1), thresholdCount: 3, TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public void RecordHit_AtThreshold_ReturnsTrueOnce_ThenResets()
    {
        var tracker = new AlertWindowTracker();
        var ruleId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        Assert.False(tracker.RecordHit(ruleId, now, 3, TimeSpan.FromSeconds(60)));
        Assert.False(tracker.RecordHit(ruleId, now.AddSeconds(1), 3, TimeSpan.FromSeconds(60)));
        Assert.True(tracker.RecordHit(ruleId, now.AddSeconds(2), 3, TimeSpan.FromSeconds(60)));

        // Threshold just fired and cleared the window — one more hit shouldn't immediately refire.
        Assert.False(tracker.RecordHit(ruleId, now.AddSeconds(3), 3, TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public void RecordHit_OldHitsOutsideWindow_AreDropped_SoThresholdNeverReached()
    {
        var tracker = new AlertWindowTracker();
        var ruleId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        Assert.False(tracker.RecordHit(ruleId, now, 3, TimeSpan.FromSeconds(10)));
        Assert.False(tracker.RecordHit(ruleId, now.AddSeconds(20), 3, TimeSpan.FromSeconds(10)));
        Assert.False(tracker.RecordHit(ruleId, now.AddSeconds(21), 3, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public void RecordHit_TracksEachRuleIndependently()
    {
        var tracker = new AlertWindowTracker();
        var ruleA = Guid.NewGuid();
        var ruleB = Guid.NewGuid();
        var now = DateTime.UtcNow;

        Assert.False(tracker.RecordHit(ruleA, now, 2, TimeSpan.FromSeconds(60)));
        Assert.False(tracker.RecordHit(ruleB, now, 2, TimeSpan.FromSeconds(60)));
        Assert.True(tracker.RecordHit(ruleA, now.AddSeconds(1), 2, TimeSpan.FromSeconds(60)));
        Assert.False(tracker.RecordHit(ruleB, now.AddSeconds(1), 3, TimeSpan.FromSeconds(60)));
    }
}
