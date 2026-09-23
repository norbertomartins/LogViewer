using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Media;

namespace LogViewer.App.Controls;

/// <summary>One mark on a <see cref="ScrollMarkerStrip"/>: a relative position (0 = first visible line, 1 = last)
/// and its color. Lower <see cref="Priority"/> wins when several marks land on the same pixel row.</summary>
public readonly record struct ScrollMarker(double Position, Brush Brush, int Priority);

/// <summary>
/// A thin overview strip drawn beside the log list (like an editor's scrollbar annotations): errors, warnings,
/// bookmarks and highlight matches as colored ticks at their relative position in the visible (filtered) lines.
/// Marks are bucketed per device-independent pixel row with a priority, so rendering cost is bounded by the strip
/// height, not the line count. Clicking reports the clicked fraction via <see cref="PositionClicked"/>.
/// </summary>
public sealed class ScrollMarkerStrip : FrameworkElement
{
    private const double MarkHeight = 2;

    private IReadOnlyList<ScrollMarker> _markers = [];

    public ScrollMarkerStrip()
    {
        Cursor = Cursors.Hand;
    }

    /// <summary>Raised with the clicked relative position (0..1).</summary>
    public event Action<double>? PositionClicked;

    /// <summary>Shared by every row so hit-testing works on the whole strip, not just on drawn ticks.</summary>
    public Brush Background { get; set; } = Brushes.Transparent;

    public void SetMarkers(IReadOnlyList<ScrollMarker> markers)
    {
        _markers = markers;
        InvalidateVisual();
    }

    /// <summary>A bare FrameworkElement has no automation peer, which would hide the strip from screen readers and
    /// UI tests; expose it as a plain custom element.</summary>
    protected override AutomationPeer OnCreateAutomationPeer() => new FrameworkElementAutomationPeer(this);

    protected override void OnRender(DrawingContext drawingContext)
    {
        var height = ActualHeight;
        var width = ActualWidth;
        drawingContext.DrawRectangle(Background, null, new Rect(0, 0, width, height));
        if (height <= MarkHeight || _markers.Count == 0)
        {
            return;
        }

        var rows = (int)Math.Ceiling(height / MarkHeight);
        var best = new ScrollMarker?[rows];
        foreach (var marker in _markers)
        {
            var row = (int)Math.Clamp(marker.Position * (rows - 1), 0, rows - 1);
            if (best[row] is not { } existing || marker.Priority < existing.Priority)
            {
                best[row] = marker;
            }
        }

        for (var row = 0; row < rows; row++)
        {
            if (best[row] is { } marker)
            {
                drawingContext.DrawRectangle(marker.Brush, null, new Rect(0, row * MarkHeight, width, MarkHeight));
            }
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (ActualHeight > 0)
        {
            PositionClicked?.Invoke(Math.Clamp(e.GetPosition(this).Y / ActualHeight, 0, 1));
            e.Handled = true;
        }
    }
}
