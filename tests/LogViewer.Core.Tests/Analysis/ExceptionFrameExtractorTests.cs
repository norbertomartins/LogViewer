using LogViewer.Core.Analysis;

namespace LogViewer.Core.Tests.Analysis;

public sealed class ExceptionFrameExtractorTests
{
    [Fact]
    public void ExtractTopFrame_ReturnsFirstFrame()
    {
        var text = "System.InvalidOperationException: boom\n   at Billing.PaymentProcessor.Charge(Decimal amount) in C:\\Billing.cs:line 42\n   at Billing.Orders.Complete()";

        Assert.Equal("Billing.PaymentProcessor.Charge", ExceptionFrameExtractor.ExtractTopFrame(text));
    }

    [Fact]
    public void ExtractTopFrame_NoFrames_ReturnsNull()
    {
        Assert.Null(ExceptionFrameExtractor.ExtractTopFrame("System.Exception: boom"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ExtractTopFrame_NullOrEmpty_ReturnsNull(string? text)
    {
        Assert.Null(ExceptionFrameExtractor.ExtractTopFrame(text));
    }
}
