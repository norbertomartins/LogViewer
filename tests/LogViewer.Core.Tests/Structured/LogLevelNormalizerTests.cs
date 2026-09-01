using LogViewer.Core.Structured;

namespace LogViewer.Core.Tests.Structured;

public sealed class LogLevelNormalizerTests
{
    [Theory]
    [InlineData("warn", "Warning")]
    [InlineData("ERR", "Error")]
    [InlineData("panic", "Fatal")]
    [InlineData("trace", "Verbose")]
    [InlineData("", "Information")]
    public void Normalize_MapsSpellings(string raw, string expected) =>
        Assert.Equal(expected, LogLevelNormalizer.Normalize(raw));

    [Theory]
    [InlineData("2026-02-15 09:00:03.000 [INFO] Payment captured", 2)]
    [InlineData("2026-02-15 09:00:03.000 [ERROR] Gateway timeout", 4)]
    [InlineData("10:00:00 WARN disk low", 3)]
    [InlineData("some line with FATAL and also WARN in it", 5)] // highest wins
    [InlineData("no level word here", null)]
    public void GuessSeverityFromLine(string line, int? expectedRank) =>
        Assert.Equal(expectedRank, LogLevelNormalizer.GuessSeverityFromLine(line));

    [Theory]
    [InlineData(2, "Fatal")]
    [InlineData(3, "Error")]
    [InlineData(4, "Warning")]
    [InlineData(6, "Information")]
    public void FromSyslogSeverity(int severity, string expected) =>
        Assert.Equal(expected, LogLevelNormalizer.FromSyslogSeverity(severity));
}
