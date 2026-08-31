using LogViewer.Core.Structured;

namespace LogViewer.Core.Tests.Structured;

public sealed class GenericJsonLogLineParserTests
{
    private readonly GenericJsonLogLineParser _parser = new();

    [Fact]
    public void TryParse_MelJsonConsoleShape_Parses()
    {
        var line = @"{""Timestamp"":""2026-01-02T03:04:05.123+00:00"",""LogLevel"":""Warning"",""Message"":""Cache miss for {Key}"",""Key"":""abc"",""EventId"":42}";

        Assert.True(_parser.TryParse(line, out var evt));
        Assert.Equal("Warning", evt!.Level);
        Assert.Equal("Cache miss for {Key}", evt.RenderedMessage);
        Assert.Equal("abc", evt.Properties["Key"]);
        Assert.Equal("42", evt.Properties["EventId"]);
    }

    [Fact]
    public void TryParse_PinoBunyanShape_LevelAliasesAndUnixMillis()
    {
        var line = @"{""level"":""error"",""time"":1704164645123,""msg"":""boom"",""hostname"":""web-1""}";

        Assert.True(_parser.TryParse(line, out var evt));
        Assert.Equal("Error", evt!.Level);
        Assert.Equal("boom", evt.RenderedMessage);
        Assert.Equal(new DateTimeOffset(2024, 1, 2, 3, 4, 5, 123, TimeSpan.Zero), evt.Timestamp);
        Assert.Equal("web-1", evt.Properties["hostname"]);
    }

    [Fact]
    public void TryParse_ExceptionField_Captured()
    {
        var line = @"{""level"":""error"",""message"":""failed"",""exception"":""System.Exception: x""}";

        Assert.True(_parser.TryParse(line, out var evt));
        Assert.Equal("System.Exception: x", evt!.Exception);
    }

    [Fact]
    public void TryParse_NestedObjectProperty_FlattenedToJson()
    {
        var line = @"{""level"":""info"",""msg"":""ok"",""user"":{""id"":1,""name"":""a""}}";

        Assert.True(_parser.TryParse(line, out var evt));
        Assert.Contains("\"id\"", evt!.Properties["user"]);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2,3]")]
    [InlineData("{}")]
    public void TryParse_NonObjectOrEmpty_ReturnsFalse(string line)
    {
        Assert.False(_parser.TryParse(line, out var evt));
        Assert.Null(evt);
    }
}
