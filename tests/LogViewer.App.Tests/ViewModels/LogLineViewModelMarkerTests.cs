using System.Windows.Media;
using LogViewer.App.Models;
using LogViewer.Core.Highlighting;
using LogViewer.Core.Structured;

namespace LogViewer.App.Tests.ViewModels;

public sealed class LogLineViewModelMarkerTests
{
    [Fact]
    public void SeverityRank_ComesFromTheStructuredLevel_ElseFromALevelWord()
    {
        var structured = new StructuredLogEvent(null, "Warning", null, "msg", null, new Dictionary<string, string>());

        Assert.Equal(LogLevelSeverity.Rank("Warning"), new LogLineViewModel(1, "{json}", structured, null, false).SeverityRank);
        Assert.Equal(LogLevelSeverity.Rank("Error"), new LogLineViewModel(2, "10:00 [ERROR] boom", null, null, false).SeverityRank);
        Assert.Null(new LogLineViewModel(3, "just text", null, null, false).SeverityRank);
    }

    [Fact]
    public void HighlightMarkerBrush_UsesTheRuleBackground_OrItsForegroundWhenBackgroundIsDefault()
    {
        var none = new LogLineViewModel(1, "x", null, null, false);
        Assert.Null(none.HighlightMarkerBrush);

        var withBackground = new LogLineViewModel(2, "x", null, new HighlightMatch(Guid.NewGuid(), "#FF000000", "#FF00FF00"), false);
        Assert.Equal(Colors.Lime, ((SolidColorBrush)withBackground.HighlightMarkerBrush!).Color);

        withBackground.ApplyMatch(null);
        Assert.Null(withBackground.HighlightMarkerBrush);
    }
}
