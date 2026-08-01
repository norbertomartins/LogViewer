using LogViewer.Core.Tailing;
using LogViewer.Core.Tests.TestUtilities;

namespace LogViewer.Core.Tests.Tailing;

public sealed class FileChangeDetectorTests
{
    [Fact]
    public void Check_FirstObservation_ReturnsNone()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText("hello\n");
        var detector = new FileChangeDetector();

        Assert.Equal(FileChangeKind.None, detector.Check(fixture.FilePath));
    }

    [Fact]
    public void Check_AfterAppendOnly_ReturnsNone()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText("hello\n");
        var detector = new FileChangeDetector();
        detector.Check(fixture.FilePath);

        fixture.AppendText("more\n");

        Assert.Equal(FileChangeKind.None, detector.Check(fixture.FilePath));
    }

    [Fact]
    public void Check_AfterTruncate_ReturnsTruncated()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText("hello world\n");
        var detector = new FileChangeDetector();
        detector.Check(fixture.FilePath);

        fixture.Truncate();

        Assert.Equal(FileChangeKind.Truncated, detector.Check(fixture.FilePath));
    }

    [Fact]
    public void Check_AfterRenameAndRecreate_ReturnsRotated()
    {
        using var fixture = new TempFileFixture();
        fixture.WriteAllText("hello\n");
        var detector = new FileChangeDetector();
        detector.Check(fixture.FilePath);

        fixture.RenameAndRecreate("new content\n");

        Assert.Equal(FileChangeKind.Rotated, detector.Check(fixture.FilePath));
    }

    [Fact]
    public void Check_WhenFileMissing_ReturnsDeleted()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid().ToString("N") + ".log");
        var detector = new FileChangeDetector();

        Assert.Equal(FileChangeKind.Deleted, detector.Check(missingPath));
    }
}
