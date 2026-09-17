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
}
