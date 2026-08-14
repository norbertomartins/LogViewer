namespace LogViewer.Mcp.Tests.TestUtilities;

/// <summary>Creates a temp file for a test and deletes the containing temp directory on dispose. Mirrors
/// LogViewer.Core.Tests' fixture of the same name — duplicated rather than shared since test projects
/// aren't meant to reference each other.</summary>
public sealed class TempFileFixture : IDisposable
{
    private readonly string _directory;

    public TempFileFixture(string fileName = "test.log")
    {
        _directory = Path.Combine(Path.GetTempPath(), "LogViewerMcpTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        FilePath = Path.Combine(_directory, fileName);
    }

    public string FilePath { get; }

    public void WriteAllText(string content) => File.WriteAllText(FilePath, content);

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
