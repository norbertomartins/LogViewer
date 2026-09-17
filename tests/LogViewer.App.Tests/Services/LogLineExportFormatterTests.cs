using System.Text.Json;
using LogViewer.App.Models;
using LogViewer.App.Services;
using LogViewer.Core.Structured;

namespace LogViewer.App.Tests.Services;

public sealed class LogLineExportFormatterTests
{
    [Fact]
    public void ToPlainText_JoinsRawTextWithNewLines()
    {
        var lines = new[]
        {
            new LogLineViewModel(1, "first line", null, null, isBookmarked: false),
            new LogLineViewModel(2, "second line", null, null, isBookmarked: false),
        };

        var text = LogLineExportFormatter.ToPlainText(lines);

        Assert.Equal($"first line{Environment.NewLine}second line", text);
    }

    [Fact]
    public void ToJson_PlainLine_FallsBackToLineNumberAndText()
    {
        var lines = new[] { new LogLineViewModel(5, "plain text", null, null, isBookmarked: false) };

        var json = LogLineExportFormatter.ToJson(lines);
        using var doc = JsonDocument.Parse(json);
        var entry = doc.RootElement[0];

        Assert.Equal(5, entry.GetProperty("lineNumber").GetInt32());
        Assert.Equal("plain text", entry.GetProperty("text").GetString());
        Assert.False(entry.TryGetProperty("level", out _));
    }

    [Fact]
    public void ToJson_StructuredLine_SerializesParsedFields()
    {
        var structured = new StructuredLogEvent(
            Timestamp: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Level: "Error",
            MessageTemplate: "Failed for {UserId}",
            RenderedMessage: "Failed for 42",
            Exception: "System.Exception: boom",
            Properties: new Dictionary<string, string> { ["UserId"] = "42" });

        var lines = new[] { new LogLineViewModel(7, "raw json text", structured, null, isBookmarked: false) };

        var json = LogLineExportFormatter.ToJson(lines);
        using var doc = JsonDocument.Parse(json);
        var entry = doc.RootElement[0];

        Assert.Equal(7, entry.GetProperty("lineNumber").GetInt32());
        Assert.Equal("Error", entry.GetProperty("level").GetString());
        Assert.Equal("Failed for 42", entry.GetProperty("message").GetString());
        Assert.Equal("System.Exception: boom", entry.GetProperty("exception").GetString());
        Assert.Equal("42", entry.GetProperty("properties").GetProperty("UserId").GetString());
        Assert.False(entry.TryGetProperty("text", out _));
    }

    [Fact]
    public void ToFormattedText_PlainLine_CopiesRawTextUnchanged()
    {
        var lines = new[] { new LogLineViewModel(1, "raw plain text", null, null, isBookmarked: false) };

        var text = LogLineExportFormatter.ToFormattedText(lines);

        Assert.Equal("raw plain text", text);
    }

    [Fact]
    public void ToFormattedText_StructuredLine_RendersTimestampLevelAndMessage_NotRawJson()
    {
        var structured = new StructuredLogEvent(
            Timestamp: new DateTimeOffset(2026, 1, 2, 3, 4, 5, 678, TimeSpan.Zero),
            Level: "Warning",
            MessageTemplate: "Retrying {Attempt}",
            RenderedMessage: "Retrying 3",
            Exception: null,
            Properties: new Dictionary<string, string> { ["Attempt"] = "3" });

        var lines = new[] { new LogLineViewModel(1, """{"@t":"2026-01-02T03:04:05.678Z","@m":"Retrying 3"}""", structured, null, isBookmarked: false) };

        var text = LogLineExportFormatter.ToFormattedText(lines);

        Assert.Equal("2026-01-02 03:04:05.678 [Warning] Retrying 3", text);
        Assert.DoesNotContain("@t", text);
    }

    [Fact]
    public void ToFormattedText_StructuredLineWithException_AppendsExceptionOnNextLine()
    {
        var structured = new StructuredLogEvent(
            Timestamp: null, Level: "Error", MessageTemplate: null, RenderedMessage: "boom",
            Exception: "System.Exception: kaboom", Properties: new Dictionary<string, string>());

        var lines = new[] { new LogLineViewModel(1, "{}", structured, null, isBookmarked: false) };

        var text = LogLineExportFormatter.ToFormattedText(lines);

        Assert.Equal($"[Error] boom{Environment.NewLine}System.Exception: kaboom", text);
    }
}
