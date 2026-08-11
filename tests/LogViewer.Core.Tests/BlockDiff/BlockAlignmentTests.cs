using LogViewer.Core.BlockDiff;
using LogViewer.Core.Structured;

namespace LogViewer.Core.Tests.BlockDiff;

public sealed class BlockAlignmentTests
{
    private static LogBlockLine Line(long lineNumber, string signature, string renderedMessage, params (string Key, string Value)[] props) =>
        new(lineNumber, signature, new StructuredLogEvent(null, "Information", null, renderedMessage, null, props.ToDictionary(p => p.Key, p => p.Value)));

    [Fact]
    public void Align_IdenticalBlocks_AllCommon_NoValueDifferences()
    {
        var left = new LogBlock([Line(1, "A", "start"), Line(2, "B", "end")], null, null, "left");
        var right = new LogBlock([Line(10, "A", "start"), Line(11, "B", "end")], null, null, "right");

        var diff = BlockAlignment.Align(left, right);

        Assert.All(diff, e => Assert.Equal(DiffLineKind.Common, e.Kind));
        Assert.All(diff, e => Assert.False(e.ValuesDiffer));
    }

    [Fact]
    public void Align_ExtraLineOnlyInLeft_IsReported()
    {
        var left = new LogBlock([Line(1, "A", "start"), Line(2, "X", "extra"), Line(3, "B", "end")], null, null, "left");
        var right = new LogBlock([Line(10, "A", "start"), Line(11, "B", "end")], null, null, "right");

        var diff = BlockAlignment.Align(left, right);

        Assert.Contains(diff, e => e.Kind == DiffLineKind.OnlyInLeft && e.Left!.Signature == "X");
    }

    [Fact]
    public void Align_ExtraLineOnlyInRight_IsReported()
    {
        var left = new LogBlock([Line(1, "A", "start"), Line(2, "B", "end")], null, null, "left");
        var right = new LogBlock([Line(10, "A", "start"), Line(11, "Y", "extra"), Line(12, "B", "end")], null, null, "right");

        var diff = BlockAlignment.Align(left, right);

        Assert.Contains(diff, e => e.Kind == DiffLineKind.OnlyInRight && e.Right!.Signature == "Y");
    }

    [Fact]
    public void Align_SameSignature_DifferentPropertyValues_MarksValuesDiffer()
    {
        var left = new LogBlock([Line(1, "A", "duration 100ms", ("DurationMs", "100"))], null, null, "left");
        var right = new LogBlock([Line(10, "A", "duration 4000ms", ("DurationMs", "4000"))], null, null, "right");

        var diff = BlockAlignment.Align(left, right);

        var entry = Assert.Single(diff);
        Assert.Equal(DiffLineKind.Common, entry.Kind);
        Assert.True(entry.ValuesDiffer);
    }

    [Fact]
    public void Align_SameSignatureAndValues_DoesNotMarkValuesDiffer()
    {
        var left = new LogBlock([Line(1, "A", "duration 100ms", ("DurationMs", "100"))], null, null, "left");
        var right = new LogBlock([Line(10, "A", "duration 100ms", ("DurationMs", "100"))], null, null, "right");

        var diff = BlockAlignment.Align(left, right);

        var entry = Assert.Single(diff);
        Assert.False(entry.ValuesDiffer);
    }
}
