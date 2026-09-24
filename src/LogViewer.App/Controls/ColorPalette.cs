using System.Globalization;

namespace LogViewer.App.Controls;

/// <summary>The colors offered by <see cref="ColorPickerButton"/> and the hex/HSV conversions its custom-color editor
/// needs. 40 suggested shades plus the 216 web-safe colors make 256 swatches.</summary>
public static class ColorPalette
{
    /// <summary>Most custom colors remembered (one row of the picker).</summary>
    public const int MaxCustomColors = 18;

    /// <summary>Grayscale plus light/base/dark shades of common accent hues — soft enough for highlight backgrounds.</summary>
    public static readonly string[] Suggested =
    [
        "#FFFFFF", "#F5F5F5", "#E0E0E0", "#BDBDBD", "#9E9E9E", "#757575", "#616161", "#424242", "#212121", "#000000",
        "#FFCDD2", "#FFE0B2", "#FFF9C4", "#DCEDC8", "#B2EBF2", "#BBDEFB", "#C5CAE9", "#E1BEE7", "#F8BBD0", "#D7CCC8",
        "#EF5350", "#FFA726", "#FFEE58", "#9CCC65", "#26C6DA", "#42A5F5", "#5C6BC0", "#AB47BC", "#EC407A", "#8D6E63",
        "#B71C1C", "#E65100", "#F57F17", "#33691E", "#00838F", "#0D47A1", "#283593", "#4A148C", "#880E4F", "#3E2723",
    ];

    /// <summary>Columns of the <see cref="WebSafe"/> grid.</summary>
    public const int WebSafeColumns = 18;

    /// <summary>The 216 web-safe colors (every mix of 00/33/66/99/CC/FF) in the classic 18 × 12 layout: six 6 × 6 blocks
    /// of one red level each (blue across, green down), reds 00–66 on the top half and 99–FF on the bottom.</summary>
    public static readonly string[] WebSafe = BuildWebSafe();

    private static string[] BuildWebSafe()
    {
        var colors = new string[216];
        for (var row = 0; row < 12; row++)
        {
            for (var col = 0; col < WebSafeColumns; col++)
            {
                var red = (row / 6 * 3) + (col / 6);
                var green = row % 6;
                var blue = col % 6;
                colors[(row * WebSafeColumns) + col] = ToHex((byte)(red * 0x33), (byte)(green * 0x33), (byte)(blue * 0x33));
            }
        }

        return colors;
    }

    public static string ToHex(byte r, byte g, byte b) => $"#{r:X2}{g:X2}{b:X2}";

    /// <summary>"#RRGGBB" (upper case) for "#RGB", "RRGGBB", "#RRGGBB" or "#AARRGGBB" (alpha dropped); null otherwise.</summary>
    public static string? NormalizeHex(string? text)
    {
        var hex = text?.Trim().TrimStart('#');
        if (hex is null || !hex.All(Uri.IsHexDigit))
        {
            return null;
        }

        hex = hex.Length switch
        {
            3 => string.Concat(hex.Select(c => new string(c, 2))),
            6 => hex,
            8 => hex[2..],
            _ => null,
        };
        return hex is null ? null : "#" + hex.ToUpperInvariant();
    }

    public static bool TryParse(string? text, out byte r, out byte g, out byte b)
    {
        r = g = b = 0;
        if (NormalizeHex(text) is not { } hex)
        {
            return false;
        }

        r = byte.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        g = byte.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        b = byte.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return true;
    }

    /// <summary>Hue in degrees [0, 360), saturation and value in [0, 1].</summary>
    public static (double Hue, double Saturation, double Value) ToHsv(byte r, byte g, byte b)
    {
        double red = r / 255.0, green = g / 255.0, blue = b / 255.0;
        var max = Math.Max(red, Math.Max(green, blue));
        var min = Math.Min(red, Math.Min(green, blue));
        var delta = max - min;

        double hue = 0;
        if (delta > 0)
        {
            hue = max == red ? 60 * (((green - blue) / delta) % 6)
                : max == green ? 60 * (((blue - red) / delta) + 2)
                : 60 * (((red - green) / delta) + 4);
        }

        return (hue < 0 ? hue + 360 : hue, max == 0 ? 0 : delta / max, max);
    }

    public static (byte R, byte G, byte B) FromHsv(double hue, double saturation, double value)
    {
        hue = ((hue % 360) + 360) % 360;
        saturation = Math.Clamp(saturation, 0, 1);
        value = Math.Clamp(value, 0, 1);

        var chroma = value * saturation;
        var x = chroma * (1 - Math.Abs((hue / 60 % 2) - 1));
        var m = value - chroma;
        var (red, green, blue) = (int)(hue / 60) switch
        {
            0 => (chroma, x, 0.0),
            1 => (x, chroma, 0.0),
            2 => (0.0, chroma, x),
            3 => (0.0, x, chroma),
            4 => (x, 0.0, chroma),
            _ => (chroma, 0.0, x),
        };

        return (ToByte(red + m), ToByte(green + m), ToByte(blue + m));
    }

    private static byte ToByte(double channel) => (byte)Math.Round(Math.Clamp(channel, 0, 1) * 255);

    /// <summary>Puts <paramref name="hex"/> first in <paramref name="customColors"/> (moving it if already there) and
    /// drops the oldest past <see cref="MaxCustomColors"/>. Returns false for an invalid color.</summary>
    public static bool Remember(IList<string> customColors, string? hex)
    {
        if (NormalizeHex(hex) is not { } normalized)
        {
            return false;
        }

        var existing = customColors.IndexOf(normalized);
        if (existing == 0)
        {
            return true;
        }

        if (existing > 0)
        {
            customColors.RemoveAt(existing);
        }

        customColors.Insert(0, normalized);
        while (customColors.Count > MaxCustomColors)
        {
            customColors.RemoveAt(customColors.Count - 1);
        }

        return true;
    }
}
