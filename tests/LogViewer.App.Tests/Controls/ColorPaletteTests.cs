using LogViewer.App.Controls;

namespace LogViewer.App.Tests.Controls;

public sealed class ColorPaletteTests
{
    [Fact]
    public void ThePaletteOffers256DistinctColors_WithEveryWebSafeColorOnce()
    {
        Assert.Equal(216, ColorPalette.WebSafe.Length);
        Assert.Equal(216, ColorPalette.WebSafe.Distinct().Count());
        Assert.All(ColorPalette.WebSafe, hex => Assert.All(hex[1..].Chunk(2), pair => Assert.Contains(new string(pair), new[] { "00", "33", "66", "99", "CC", "FF" })));
        Assert.Equal(256, ColorPalette.Suggested.Length + ColorPalette.WebSafe.Length);

        // Classic layout: black top-left, blue across the first block, white bottom-right.
        Assert.Equal("#000000", ColorPalette.WebSafe[0]);
        Assert.Equal("#0000FF", ColorPalette.WebSafe[5]);
        Assert.Equal("#330000", ColorPalette.WebSafe[6]);
        Assert.Equal("#FFFFFF", ColorPalette.WebSafe[^1]);
    }

    [Theory]
    [InlineData("#abc", "#AABBCC")]
    [InlineData("12ab9f", "#12AB9F")]
    [InlineData(" #12AB9F ", "#12AB9F")]
    [InlineData("#FF12AB9F", "#12AB9F")]
    [InlineData("#12AB9", null)]
    [InlineData("#GG0000", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void NormalizeHex_AcceptsTheUsualSpellings(string? text, string? expected) =>
        Assert.Equal(expected, ColorPalette.NormalizeHex(text));

    [Theory]
    [InlineData("#FF0000", 0, 1, 1)]
    [InlineData("#00FF00", 120, 1, 1)]
    [InlineData("#0000FF", 240, 1, 1)]
    [InlineData("#808080", 0, 0, 0.502)]
    [InlineData("#000000", 0, 0, 0)]
    public void ToHsv_MatchesKnownColors(string hex, double hue, double saturation, double value)
    {
        ColorPalette.TryParse(hex, out var r, out var g, out var b);
        var hsv = ColorPalette.ToHsv(r, g, b);

        Assert.Equal(hue, hsv.Hue, 1);
        Assert.Equal(saturation, hsv.Saturation, 2);
        Assert.Equal(value, hsv.Value, 2);
    }

    [Fact]
    public void HsvRoundTrips_EveryWebSafeColor()
    {
        foreach (var hex in ColorPalette.WebSafe.Concat(ColorPalette.Suggested))
        {
            ColorPalette.TryParse(hex, out var r, out var g, out var b);
            var (hue, saturation, value) = ColorPalette.ToHsv(r, g, b);
            var back = ColorPalette.FromHsv(hue, saturation, value);
            Assert.Equal(hex, ColorPalette.ToHex(back.R, back.G, back.B));
        }
    }

    [Fact]
    public void Remember_PutsTheColorFirst_WithoutDuplicates_AndKeepsAtMostOneRow()
    {
        var colors = new List<string>();

        Assert.True(ColorPalette.Remember(colors, "#112233"));
        Assert.True(ColorPalette.Remember(colors, "abc"));
        Assert.True(ColorPalette.Remember(colors, "#112233"));
        Assert.False(ColorPalette.Remember(colors, "nope"));
        Assert.Equal(["#112233", "#AABBCC"], colors);

        for (var i = 0; i < 30; i++)
        {
            ColorPalette.Remember(colors, ColorPalette.ToHex((byte)i, 0, 0));
        }

        Assert.Equal(ColorPalette.MaxCustomColors, colors.Count);
        Assert.Equal("#1D0000", colors[0]);
    }

    [Fact]
    public void LoadCustomColors_KeepsOrder_DropsInvalidAndDuplicates_AndCapsAtOneRow()
    {
        ColorPickerButton.LoadCustomColors(["#112233", "nope", "#aabbcc", "#112233", .. Enumerable.Range(0, 30).Select(i => ColorPalette.ToHex(0, 0, (byte)i))]);

        Assert.Equal(ColorPalette.MaxCustomColors, ColorPickerButton.CustomColors.Count);
        Assert.Equal(["#112233", "#AABBCC", "#000000"], ColorPickerButton.CustomColors.Take(3));
        Assert.Equal(ColorPickerButton.CustomColors, ColorPickerButton.CustomSwatches.Select(s => s.Hex));

        ColorPickerButton.LoadCustomColors([]);
        Assert.Empty(ColorPickerButton.CustomColors);
    }
}
