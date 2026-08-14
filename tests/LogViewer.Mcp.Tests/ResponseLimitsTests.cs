namespace LogViewer.Mcp.Tests;

public sealed class ResponseLimitsTests
{
    [Fact]
    public void ClampRows_RequestWithinConfiguredCap_ReturnsRequested()
    {
        ResponseLimits.Configure(ResponseLimits.DefaultHardMaxRows, ResponseLimits.DefaultHardMaxTextLength);

        Assert.Equal(50, ResponseLimits.ClampRows(50));
    }

    [Fact]
    public void ClampRows_RequestAboveConfiguredCap_IsClamped()
    {
        ResponseLimits.Configure(20, ResponseLimits.DefaultHardMaxTextLength);

        Assert.Equal(20, ResponseLimits.ClampRows(1000));
    }

    [Fact]
    public void ClampRows_NonPositiveRequest_DefaultsToConfiguredCap()
    {
        ResponseLimits.Configure(30, ResponseLimits.DefaultHardMaxTextLength);

        Assert.Equal(30, ResponseLimits.ClampRows(0));
        Assert.Equal(30, ResponseLimits.ClampRows(-5));
    }

    [Fact]
    public void Configure_ZeroOrNegativeValues_FallBackToDefaults()
    {
        ResponseLimits.Configure(0, -1);

        Assert.Equal(ResponseLimits.DefaultHardMaxRows, ResponseLimits.ClampRows(int.MaxValue));
    }

    [Fact]
    public void Truncate_TextWithinLimit_ReturnsUnchanged()
    {
        ResponseLimits.Configure(ResponseLimits.DefaultHardMaxRows, ResponseLimits.DefaultHardMaxTextLength);

        Assert.Equal("short", ResponseLimits.Truncate("short"));
    }

    [Fact]
    public void Truncate_TextAboveConfiguredLimit_IsTruncatedWithMarker()
    {
        ResponseLimits.Configure(ResponseLimits.DefaultHardMaxRows, 10);

        var result = ResponseLimits.Truncate("this text is definitely too long");

        Assert.StartsWith("this text ", result);
        Assert.EndsWith("(truncated)", result);
    }

    [Fact]
    public void Truncate_NullOrEmpty_ReturnsEmpty()
    {
        ResponseLimits.Configure(ResponseLimits.DefaultHardMaxRows, ResponseLimits.DefaultHardMaxTextLength);

        Assert.Equal(string.Empty, ResponseLimits.Truncate(null));
        Assert.Equal(string.Empty, ResponseLimits.Truncate(string.Empty));
    }
}
