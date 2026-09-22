using System.Windows.Documents;
using LogViewer.App.Converters;
using LogViewer.Core.Structured;

namespace LogViewer.App.Tests.Converters;

public sealed class StructuredInlinesConverterTests
{
    private readonly StructuredInlinesConverter _converter = new();

    [Fact]
    public void Convert_WithColorizeDisabled_ReturnsNull()
    {
        var evt = Event("Hello {Name}", "Hello world", ("Name", "world"));

        var result = _converter.Convert([evt, false], typeof(object), null, null!);

        Assert.Null(result);
    }

    [Fact]
    public void Convert_WithNoTemplate_ReturnsNull_SoPlainTextBindingTakesOver()
    {
        // No MessageTemplate means SplitIntoSegments returns a single literal segment, which the
        // converter deliberately treats as "nothing to colorize" to avoid needless Inline allocations.
        var evt = Event(template: null, "a pre-rendered message", []);

        var result = _converter.Convert([evt, true], typeof(object), null, null!);

        Assert.Null(result);
    }

    [Fact]
    public void Convert_WithATemplate_ColorizesEachValueSegment_LeavingLiteralsPlain()
    {
        var evt = Event("User {UserId} logged in from {Ip}", "User 42 logged in from 10.0.0.1",
            ("UserId", "42"), ("Ip", "10.0.0.1"));

        var result = _converter.Convert([evt, true], typeof(object), null, null!) as List<Inline>;

        Assert.NotNull(result);
        var runs = result.Cast<Run>().ToList();
        Assert.Equal(["User ", "42", " logged in from ", "10.0.0.1"], runs.Select(r => r.Text).ToArray());

        // Value segments get their property's palette brush; literal segments never get involved with
        // the palette at all (they're plain Runs with whatever Foreground the TextBlock inherits).
        Assert.Equal(StructuredValueColorPalette.GetBrush("UserId"), runs[1].Foreground);
        Assert.Equal(StructuredValueColorPalette.GetBrush("Ip"), runs[3].Foreground);
    }

    [Fact]
    public void Convert_SamePropertyName_AlwaysGetsTheSameColor()
    {
        var evt = Event("{A} and {A} again", "x and x again", ("A", "x"));

        var result = (_converter.Convert([evt, true], typeof(object), null, null!) as List<Inline>)!.Cast<Run>().ToList();

        var firstValueRun = result[0];
        var secondValueRun = result[2];
        Assert.Equal(firstValueRun.Foreground, secondValueRun.Foreground);
    }

    [Fact]
    public void Convert_WithWrongBindingShape_ReturnsNull()
    {
        var result = _converter.Convert(["not an event", true], typeof(object), null, null!);
        Assert.Null(result);
    }

    [Fact]
    public void ConvertBack_ThrowsNotSupported()
    {
        Assert.Throws<NotSupportedException>(() => _converter.ConvertBack(new object(), [], null, null!));
    }

    private static StructuredLogEvent Event(string? template, string rendered, params (string Name, string Value)[] properties) =>
        new(DateTimeOffset.UtcNow, "Information", template, rendered, Exception: null,
            Properties: properties.ToDictionary(p => p.Name, p => p.Value));
}
