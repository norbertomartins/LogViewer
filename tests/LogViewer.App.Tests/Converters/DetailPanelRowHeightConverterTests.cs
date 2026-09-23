using System.Windows;
using LogViewer.App.Converters;

namespace LogViewer.App.Tests.Converters;

public sealed class DetailPanelRowHeightConverterTests
{
    private readonly DetailPanelRowHeightConverter _converter = new();

    [Fact]
    public void Convert_WithStructuredDetail_UsesThePersistedHeight()
    {
        var result = (GridLength)_converter.Convert([180d, new object()], typeof(GridLength), null, null!);
        Assert.Equal(180d, result.Value);
    }

    [Fact]
    public void Convert_WithNoStructuredDetail_CollapsesToZero()
    {
        var result = (GridLength)_converter.Convert([180d, null!], typeof(GridLength), null, null!);
        Assert.Equal(0d, result.Value);
    }

    [Fact]
    public void Convert_WithAMissingOrWrongTypedHeight_FallsBackTo220_WhenThereIsStructuredDetail()
    {
        var result = (GridLength)_converter.Convert([null!, new object()], typeof(GridLength), null, null!);
        Assert.Equal(220d, result.Value);
    }

    [Fact]
    public void Convert_WhenTheDetailPathCannotResolve_CollapsesToZero()
    {
        // No selected line: the "SelectedLine.Structured" binding yields UnsetValue rather than null.
        var result = (GridLength)_converter.Convert([180d, DependencyProperty.UnsetValue], typeof(GridLength), null, null!);
        Assert.Equal(0d, result.Value);
    }

    [Fact]
    public void Convert_WithNoBindingsAtAll_CollapsesToZero()
    {
        var result = (GridLength)_converter.Convert([], typeof(GridLength), null, null!);
        Assert.Equal(0d, result.Value);
    }

    [Fact]
    public void ConvertBack_ThrowsNotSupported()
    {
        Assert.Throws<NotSupportedException>(() => _converter.ConvertBack(new object(), [], null, null!));
    }
}
