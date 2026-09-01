using System.IO;
using LogViewer.App.Tests.TestUtilities;
using LogViewer.App.ViewModels;

namespace LogViewer.App.Tests.ViewModels;

public sealed class OpenMergedSourcesViewModelTests : IDisposable
{
    private readonly TempDirectoryFixture _dirA = new();
    private readonly TempDirectoryFixture _dirB = new();

    [Fact]
    public void ResolveFiles_CombinesLooseFilesFromDifferentFolders()
    {
        var a = _dirA.CreateFile("one.log", "x");
        var b = _dirB.CreateFile("two.log", "y");
        var vm = new OpenMergedSourcesViewModel();
        vm.Entries.Add(new MergeSourceEntry(IsFolder: false, Path.GetFullPath(a), Pattern: null));
        vm.Entries.Add(new MergeSourceEntry(IsFolder: false, Path.GetFullPath(b), Pattern: null));

        Assert.True(vm.IsValid);
        Assert.Equal(new[] { Path.GetFullPath(a), Path.GetFullPath(b) }, vm.ResolveFiles());
    }

    [Fact]
    public void ResolveFiles_ExpandsFolderEntriesByPattern_AndDeduplicates()
    {
        _dirA.CreateFile("app.log", "1");
        _dirA.CreateFile("app.2.log", "2");
        _dirA.CreateFile("notes.txt", "skip");
        var loose = _dirB.CreateFile("extra.log", "3");

        var vm = new OpenMergedSourcesViewModel();
        vm.Entries.Add(new MergeSourceEntry(IsFolder: true, _dirA.DirectoryPath, "*.log"));
        vm.Entries.Add(new MergeSourceEntry(IsFolder: false, Path.GetFullPath(loose), Pattern: null));
        // A second folder entry pointing at the same dir/pattern must not double the files.
        vm.Entries.Add(new MergeSourceEntry(IsFolder: true, _dirA.DirectoryPath, "*.log"));

        var resolved = vm.ResolveFiles();

        Assert.Equal(3, resolved.Count);
        Assert.Contains(Path.Combine(_dirA.DirectoryPath, "app.log"), resolved);
        Assert.Contains(Path.Combine(_dirA.DirectoryPath, "app.2.log"), resolved);
        Assert.Contains(Path.GetFullPath(loose), resolved);
        Assert.DoesNotContain(Path.Combine(_dirA.DirectoryPath, "notes.txt"), resolved);
    }

    [Fact]
    public void IsValid_False_WhenFewerThanTwoFilesResolve()
    {
        var vm = new OpenMergedSourcesViewModel();
        vm.Entries.Add(new MergeSourceEntry(IsFolder: false, Path.Combine(_dirA.DirectoryPath, "only.log"), Pattern: null));

        Assert.False(vm.IsValid);
    }

    public void Dispose()
    {
        _dirA.Dispose();
        _dirB.Dispose();
    }
}
