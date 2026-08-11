using LogViewer.Core.BlockDiff;
using LogViewer.Core.Structured;

namespace LogViewer.Core.Tests.BlockDiff;

public sealed class MessageSignatureTests
{
    private static StructuredLogEvent Evt(string? messageTemplate, string renderedMessage, params (string Key, string Value)[] props) =>
        new(null, "Information", messageTemplate, renderedMessage, null, props.ToDictionary(p => p.Key, p => p.Value));

    [Fact]
    public void Compute_WithMessageTemplate_IgnoresPropertyValues()
    {
        var a = Evt("User {UserId} logged in", "User 1 logged in", ("UserId", "1"));
        var b = Evt("User {UserId} logged in", "User 999 logged in", ("UserId", "999"));

        Assert.Equal(MessageSignature.Compute(a), MessageSignature.Compute(b));
    }

    [Fact]
    public void Compute_DifferentTemplates_ProduceDifferentSignatures()
    {
        var a = Evt("User {UserId} logged in", "User 1 logged in");
        var b = Evt("User {UserId} logged out", "User 1 logged out");

        Assert.NotEqual(MessageSignature.Compute(a), MessageSignature.Compute(b));
    }

    [Fact]
    public void Compute_NoTemplate_MasksRenderedMessage_SoDifferentValuesCollapse()
    {
        var a = Evt(null, "Request took 120ms for user 42", ("DurationMs", "120"), ("UserId", "42"));
        var b = Evt(null, "Request took 4500ms for user 7", ("DurationMs", "4500"), ("UserId", "7"));

        Assert.Equal(MessageSignature.Compute(a), MessageSignature.Compute(b));
    }

    [Fact]
    public void Compute_NoTemplate_DifferentPropertyKeySets_ProduceDifferentSignatures()
    {
        var a = Evt(null, "Operation completed", ("DurationMs", "1"));
        var b = Evt(null, "Operation completed", ("Status", "ok"));

        Assert.NotEqual(MessageSignature.Compute(a), MessageSignature.Compute(b));
    }

    [Fact]
    public void Mask_Guid_IsReplaced()
    {
        Assert.Equal("id=<guid> done", MessageSignature.Mask("id=3fa85f64-5717-4562-b3fc-2c963f66afa6 done"));
    }

    [Fact]
    public void Mask_IpAddress_IsReplaced()
    {
        Assert.Equal("client <ip> connected", MessageSignature.Mask("client 192.168.1.100 connected"));
    }

    [Fact]
    public void Mask_IsoDateTime_IsReplaced()
    {
        Assert.Equal("started at <datetime>", MessageSignature.Mask("started at 2026-01-01T12:30:45.123Z"));
    }

    [Fact]
    public void Mask_BareTime_IsReplaced()
    {
        Assert.Equal("elapsed <time>", MessageSignature.Mask("elapsed 00:01:23.456"));
    }

    [Fact]
    public void Mask_QuotedString_IsReplaced()
    {
        Assert.Equal("path <str> opened", MessageSignature.Mask("path \"C:\\logs\\app.log\" opened"));
    }

    [Fact]
    public void Mask_HexLiteral_IsReplaced()
    {
        Assert.Equal("address <hex>", MessageSignature.Mask("address 0x1A2B3C4D"));
    }

    [Fact]
    public void Mask_LongAlphanumericId_IsReplaced()
    {
        Assert.Equal("token <id>", MessageSignature.Mask("token ab12cd34ef56"));
    }

    [Fact]
    public void Mask_PlainNumber_IsReplaced()
    {
        Assert.Equal("retry count <num>", MessageSignature.Mask("retry count 3"));
    }

    [Fact]
    public void Mask_PlainWord_IsLeftUntouched()
    {
        Assert.Equal("Authentication failed for user", MessageSignature.Mask("Authentication failed for user"));
    }
}
