using System.Drawing;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;

namespace LogViewer.UITests.TestUtilities;

/// <summary>
/// Pixel-sampling helpers shared by UI tests that assert on actual rendered colors (not just bound
/// values) — e.g. confirming a highlight rule's color survived ring-buffer churn, or that a theme's
/// palette reached the screen rather than a control silently falling back to a default style.
/// </summary>
public static class PixelColorHelpers
{
    /// <summary>A handful of sampled pixels that are all pure black means the desktop isn't actually being
    /// composited/captured in this session (e.g. a disconnected RDP session) rather than the app genuinely
    /// rendering an all-black window — no real WPF window is literally 0,0,0 everywhere (title bar text,
    /// borders, etc. always break that up).</summary>
    public static bool ScreenCaptureIsUnavailable(AutomationElement window)
    {
        using var image = Capture.Element(window);
        var bitmap = image.Bitmap;
        var points = new (int X, int Y)[]
        {
            (bitmap.Width / 4, bitmap.Height / 4),
            (bitmap.Width / 2, bitmap.Height / 2),
            (3 * bitmap.Width / 4, 3 * bitmap.Height / 4),
        };
        return points.All(p => bitmap.GetPixel(p.X, p.Y) is { R: 0, G: 0, B: 0 });
    }

    /// <summary>True when an element's reported bounds are plausibly inside the window — false for the
    /// disconnected/reused-peer symptom of a stale AutomationElement (e.g. Y off by over a thousand
    /// pixels), which can show up even on a freshly re-queried element.</summary>
    public static bool RectLooksSane(Rectangle elementRect, Rectangle windowRect) =>
        elementRect.Top >= windowRect.Top - 5 && elementRect.Bottom <= windowRect.Bottom + 5
        && elementRect.Left >= windowRect.Left - 5 && elementRect.Height is > 0 and < 200;

    /// <summary>Scans an element's rectangular region within an already-captured whole-window image for
    /// any pixel matching <paramref name="predicate"/>, converting the element's screen-coordinate
    /// <c>BoundingRectangle</c> into pixel offsets within that image (accounting for a DPI scale factor
    /// between logical bounds and physical pixels). Scanning the whole region rather than one computed
    /// pixel tolerates DPI-rounding imprecision that could otherwise land exactly on a text glyph or
    /// border pixel instead of the element's own background.</summary>
    public static bool RegionContainsColor(CaptureImage windowImage, AutomationElement window, AutomationElement element, Func<Color, bool> predicate)
    {
        var bitmap = windowImage.Bitmap;
        var windowRect = window.Properties.BoundingRectangle.Value;
        var elementRect = element.Properties.BoundingRectangle.Value;
        var scaleX = bitmap.Width / windowRect.Width;
        var scaleY = bitmap.Height / windowRect.Height;

        // Inset well inside the element's nominal bounds: horizontally, skip the bookmark-glyph/line-number
        // columns on the left (narrow text, not what we're checking) and stay short of the right edge;
        // vertically, keep to the middle band. This tolerates a few pixels of DPI-rounding error without
        // the scan bleeding into a neighboring row or the window chrome.
        var elementHeight = (elementRect.Bottom - elementRect.Top) * scaleY;
        var left = Math.Clamp((int)((elementRect.Left - windowRect.Left) * scaleX + (elementRect.Width * scaleX * 0.15)), 0, bitmap.Width - 1);
        var right = Math.Clamp((int)((elementRect.Right - windowRect.Left) * scaleX - 5), 0, bitmap.Width - 1);
        var top = Math.Clamp((int)((elementRect.Top - windowRect.Top) * scaleY + elementHeight * 0.3), 0, bitmap.Height - 1);
        var bottom = Math.Clamp((int)((elementRect.Bottom - windowRect.Top) * scaleY - elementHeight * 0.3), 0, bitmap.Height - 1);

        for (var y = top; y <= bottom; y++)
        {
            for (var x = left; x <= right; x += 3)
            {
                if (predicate(bitmap.GetPixel(x, y)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>True when <paramref name="actual"/> is within <paramref name="tolerance"/> per channel of
    /// <paramref name="expected"/> — used to match a sampled pixel against a theme's nominal hex color
    /// without requiring an exact match (anti-aliasing/ClearType blending shifts individual pixels).</summary>
    public static bool IsColorNear(Color actual, Color expected, int tolerance) =>
        Math.Abs(actual.R - expected.R) <= tolerance
        && Math.Abs(actual.G - expected.G) <= tolerance
        && Math.Abs(actual.B - expected.B) <= tolerance;
}
