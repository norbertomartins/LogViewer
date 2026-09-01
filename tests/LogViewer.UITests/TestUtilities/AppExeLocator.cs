using System.IO;

namespace LogViewer.UITests.TestUtilities;

/// <summary>Locates the built <c>LogViewer.App.exe</c> relative to the repo root, without a
/// ProjectReference (these tests drive the app out-of-process via UI Automation).</summary>
public static class AppExeLocator
{
    /// <summary>Absolute path to the repository root (the folder containing <c>LogViewer.slnx</c>).</summary>
    public static string RepoRoot() => FindRepoRoot(AppContext.BaseDirectory)
        ?? throw new InvalidOperationException($"Could not locate LogViewer.slnx above '{AppContext.BaseDirectory}'.");

    /// <summary>Absolute path to a file under <c>samples/</c>.</summary>
    public static string Sample(params string[] relativeParts) =>
        Path.Combine(new[] { RepoRoot(), "samples" }.Concat(relativeParts).ToArray());

    public static string Find()
    {
        var repoRoot = RepoRoot();

        foreach (var configuration in new[] { "Debug", "Release" })
        {
            var candidate = Path.Combine(repoRoot, "src", "LogViewer.App", "bin", configuration, "net10.0-windows", "LogViewer.App.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException(
            $"LogViewer.App.exe was not found under '{Path.Combine(repoRoot, "src", "LogViewer.App", "bin")}'. " +
            "Build LogViewer.App (Debug or Release) before running the UI tests.");
    }

    private static string? FindRepoRoot(string startDirectory)
    {
        var directory = new DirectoryInfo(startDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "LogViewer.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
