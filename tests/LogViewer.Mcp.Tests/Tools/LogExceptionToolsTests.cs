using LogViewer.Mcp.Tests.TestUtilities;
using LogViewer.Mcp.Tools;

namespace LogViewer.Mcp.Tests.Tools;

public sealed class LogExceptionToolsTests
{
    public LogExceptionToolsTests() => ResponseLimits.Configure(ResponseLimits.DefaultHardMaxRows, ResponseLimits.DefaultHardMaxTextLength);

    [Fact]
    public async Task ExceptionGroups_GroupsPlainTextTraces_AndCapsLineNumbers()
    {
        using var fixture = new TempFileFixture();
        var content = string.Concat(Enumerable.Range(1, 30).Select(i =>
            $"2026-01-01 00:00:{i % 60:00} ERROR attempt {i}\nSystem.TimeoutException: timed out after {i}ms\n   at Db.Query() in C:\\src\\Db.cs:line {i}\n"));
        fixture.WriteAllText(content + "System.IO.IOException: disk\n   at Io.Write()\n");

        var tools = new LogExceptionTools();
        var result = await tools.ExceptionGroups(fixture.FilePath, topN: 1, CancellationToken.None);

        var group = Assert.Single(result);
        Assert.Equal("System.TimeoutException", group.ExceptionType);
        Assert.Equal(30, group.Count);
        Assert.Equal(20, group.LineNumbers.Count);
    }
}
