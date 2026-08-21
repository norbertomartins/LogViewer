using System.IO;

namespace LogViewer.App.Tests.TestUtilities;

/// <summary>Creates a temp directory for a test (optionally with a starter file) and deletes it on dispose.</summary>
public sealed class TempDirectoryFixture : IDisposable
{
    public TempDirectoryFixture()
    {
        DirectoryPath = Path.Combine(Path.GetTempPath(), "LogViewerAppTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
    }

    public string DirectoryPath { get; }

    public string CreateFile(string fileName, string content = "")
    {
        var path = Path.Combine(DirectoryPath, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(DirectoryPath, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort cleanup — a lingering handle on Windows shouldn't fail the test run.
        }
    }
}
