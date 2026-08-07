using LogViewer.Core.Structured;

namespace LogViewer.Core.Tests.Structured;

public sealed class SerilogEventParserTests
{
    [Fact]
    public void TryParse_Clef_ParsesTimestampLevelAndRenderedMessage()
    {
        var line = @"{""@t"":""2026-01-01T12:30:45.1234567Z"",""@mt"":""User {UserId} logged in from {Ip}"",""@l"":""Information"",""UserId"":42,""Ip"":""1.2.3.4""}";

        Assert.True(SerilogEventParser.TryParse(line, out var evt));
        Assert.NotNull(evt);
        Assert.Equal(new DateTimeOffset(2026, 1, 1, 12, 30, 45, TimeSpan.Zero).AddTicks(1234567), evt!.Timestamp);
        Assert.Equal("Information", evt.Level);
        Assert.Equal("User 42 logged in from 1.2.3.4", evt.RenderedMessage);
        Assert.Equal("42", evt.Properties["UserId"]);
        Assert.Equal("1.2.3.4", evt.Properties["Ip"]);
        Assert.Null(evt.Exception);
    }

    [Fact]
    public void TryParse_Clef_OmittedLevel_DefaultsToInformation()
    {
        var line = @"{""@t"":""2026-01-01T00:00:00Z"",""@mt"":""started""}";

        Assert.True(SerilogEventParser.TryParse(line, out var evt));
        Assert.Equal("Information", evt!.Level);
    }

    [Fact]
    public void TryParse_Clef_ExceptionField_IsCaptured()
    {
        var line = @"{""@t"":""2026-01-01T00:00:00Z"",""@mt"":""Failed to process {OrderId}"",""@l"":""Error"",""@x"":""System.Exception: boom\n   at Foo.Bar()"",""OrderId"":7}";

        Assert.True(SerilogEventParser.TryParse(line, out var evt));
        Assert.Equal("Error", evt!.Level);
        Assert.StartsWith("System.Exception: boom", evt.Exception);
        Assert.Equal("7", evt.Properties["OrderId"]);
    }

    [Fact]
    public void TryParse_StandardJsonFormatter_WithNestedProperties_Parses()
    {
        var line = @"{""Timestamp"":""2026-01-01T12:30:45.1234567+00:00"",""Level"":""Warning"",""MessageTemplate"":""Retry {Count}"",""Properties"":{""Count"":3}}";

        Assert.True(SerilogEventParser.TryParse(line, out var evt));
        Assert.Equal("Warning", evt!.Level);
        Assert.Equal("Retry 3", evt.RenderedMessage);
        Assert.Equal("3", evt.Properties["Count"]);
    }

    [Fact]
    public void TryParse_StandardJsonFormatter_PrefersExplicitRenderedMessage()
    {
        var line = @"{""Timestamp"":""2026-01-01T00:00:00Z"",""Level"":""Information"",""MessageTemplate"":""Retry {Count}"",""RenderedMessage"":""Retry 3 (custom)"",""Properties"":{""Count"":3}}";

        Assert.True(SerilogEventParser.TryParse(line, out var evt));
        Assert.Equal("Retry 3 (custom)", evt!.RenderedMessage);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("plain text log line, not JSON")]
    [InlineData("{not valid json")]
    [InlineData(@"{""foo"":""bar""}")]
    [InlineData("[1,2,3]")]
    public void TryParse_NonSerilogInput_ReturnsFalse(string line)
    {
        Assert.False(SerilogEventParser.TryParse(line, out var evt));
        Assert.Null(evt);
    }

    [Fact]
    public void TryParse_PositionalTemplateTokens_Substitute()
    {
        var line = @"{""@t"":""2026-01-01T00:00:00Z"",""@mt"":""{0} then {1}"",""0"":""first"",""1"":""second""}";

        Assert.True(SerilogEventParser.TryParse(line, out var evt));
        Assert.Equal("first then second", evt!.RenderedMessage);
    }

    [Fact]
    public void TryParse_DestructuredTemplateToken_Substitutes()
    {
        var line = @"{""@t"":""2026-01-01T00:00:00Z"",""@mt"":""Order {@Order}"",""Order"":{""Id"":1}}";

        Assert.True(SerilogEventParser.TryParse(line, out var evt));
        Assert.Contains("Id", evt!.RenderedMessage);
    }
}
