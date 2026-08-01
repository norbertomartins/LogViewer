namespace LogViewer.Core.Tests.TestUtilities;

/// <summary>Creates a temp file for a test and deletes the containing temp directory on dispose.</summary>
public sealed class TempFileFixture : IDisposable
{
    private readonly string _directory;

    public TempFileFixture(string fileName = "test.log")
    {
        _directory = Path.Combine(Path.GetTempPath(), "LogViewerTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        FilePath = Path.Combine(_directory, fileName);
    }

    public string FilePath { get; }

    public void WriteAllText(string content) => File.WriteAllText(FilePath, content);

    public void AppendText(string content)
    {
        using var stream = new FileStream(FilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        using var writer = new StreamWriter(stream);
        writer.Write(content);
    }

    public void Truncate()
    {
        using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        stream.SetLength(0);
    }

    public void RenameAndRecreate(string newContent)
    {
        var rotatedPath = FilePath + ".1";
        File.Delete(rotatedPath);
        File.Move(FilePath, rotatedPath);
        File.WriteAllText(FilePath, newContent);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup — a lingering handle on Windows shouldn't fail the test run.
        }
    }
}
