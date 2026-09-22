using LogViewer.App.Converters;

namespace LogViewer.App.Tests.Converters;

public sealed class VolumeBinBarHeightConverterTests
{
    private readonly VolumeBinBarHeightConverter _converter = new();

    [Fact]
    public void Convert_ProportionsHeightToTheSegmentsShareOfTheMax()
    {
        var half = _converter.Convert([5, 10], typeof(double), null, null!);
        Assert.Equal(22d, half);

        var full = _converter.Convert([10, 10], typeof(double), null, null!);
        Assert.Equal(44d, full);
    }

    [Fact]
    public void Convert_ClampsAtOneMinimumPixel_SoANonZeroCountIsNeverInvisible()
    {
        var result = _converter.Convert([1, 100_000], typeof(double), null, null!);
        Assert.Equal(1d, result);
    }

    [Fact]
    public void Convert_WithZeroCount_ReturnsZero()
    {
        var result = _converter.Convert([0, 10], typeof(double), null, null!);
        Assert.Equal(0d, result);
    }

    [Fact]
    public void Convert_WithZeroMax_ReturnsZero_AvoidingDivideByZero()
    {
        var result = _converter.Convert([5, 0], typeof(double), null, null!);
        Assert.Equal(0d, result);
    }

    [Fact]
    public void Convert_HonorsACustomMaxHeightParameter()
    {
        var result = _converter.Convert([10, 10], typeof(double), "100", null!);
        Assert.Equal(100d, result);
    }

    [Fact]
    public void Convert_WithWrongBindingShape_ReturnsZero()
    {
        var result = _converter.Convert(["not", "ints"], typeof(double), null, null!);
        Assert.Equal(0d, result);
    }

    [Fact]
    public void ConvertBack_ThrowsNotSupported()
    {
        Assert.Throws<NotSupportedException>(() => _converter.ConvertBack(new object(), [], null, null!));
    }
}
