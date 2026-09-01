using System.IO;
using LogViewer.App.Tests.TestUtilities;
using LogViewer.Core.Configuration;

namespace LogViewer.App.Tests.ViewModels;

public sealed class SessionProfileTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();

    [Fact]
    public void SaveSessionProfile_SnapshotsOpenDocuments_AndListsTheName()
    {
        var a = _tempDir.CreateFile("a.log", "x\n");
        var b = _tempDir.CreateFile("b.log", "y\n");
        var (viewModel, settings) = MainViewModelFactory.Create();
        viewModel.OpenPath(a);
        viewModel.OpenPath(b);

        viewModel.SaveSessionProfile("Incident");

        var profile = Assert.Single(settings.SessionProfiles);
        Assert.Equal("Incident", profile.Name);
        Assert.Equal(2, profile.Sources.Count);
        Assert.Contains("Incident", viewModel.SessionProfileNames);
        viewModel.Dispose();
    }

    [Fact]
    public void SaveSessionProfile_SameName_Replaces()
    {
        var a = _tempDir.CreateFile("a.log", "x\n");
        var (viewModel, settings) = MainViewModelFactory.Create();
        viewModel.OpenPath(a);

        viewModel.SaveSessionProfile("Dev");
        viewModel.OpenPath(_tempDir.CreateFile("c.log", "z\n"));
        viewModel.SaveSessionProfile("Dev");

        var profile = Assert.Single(settings.SessionProfiles);
        Assert.Equal(2, profile.Sources.Count);
        viewModel.Dispose();
    }

    [Fact]
    public void LoadSessionProfile_ClosesCurrentDocuments_AndOpensTheProfileSet()
    {
        var a = _tempDir.CreateFile("a.log", "x\n");
        var b = _tempDir.CreateFile("b.log", "y\n");
        var (viewModel, _) = MainViewModelFactory.Create();
        viewModel.OpenPath(a);
        viewModel.SaveSessionProfile("OnlyA");

        viewModel.OpenPath(b);
        Assert.Equal(2, viewModel.Documents.Count);

        viewModel.LoadSessionProfileCommand.Execute("OnlyA");

        var doc = Assert.Single(viewModel.Documents);
        Assert.Equal(Path.GetFullPath(a), doc.SourcePath, ignoreCase: true);
        viewModel.Dispose();
    }

    [Fact]
    public void LoadSessionProfile_RestoresTextFilter()
    {
        var a = _tempDir.CreateFile("a.log", "x\n");
        var (viewModel, _) = MainViewModelFactory.Create();
        var doc = viewModel.OpenPath(a);
        doc.TextFilterPattern = "boom";
        doc.TextFilterExclude = true;

        viewModel.SaveSessionProfile("Filtered");
        viewModel.LoadSessionProfileCommand.Execute("Filtered");

        var restored = Assert.Single(viewModel.Documents);
        Assert.Equal("boom", restored.TextFilterPattern);
        Assert.True(restored.TextFilterExclude);
        viewModel.Dispose();
    }

    [Fact]
    public void DeleteSessionProfile_RemovesIt()
    {
        var a = _tempDir.CreateFile("a.log", "x\n");
        var (viewModel, settings) = MainViewModelFactory.Create();
        viewModel.OpenPath(a);
        viewModel.SaveSessionProfile("Temp");

        viewModel.DeleteSessionProfileCommand.Execute("Temp");

        Assert.Empty(settings.SessionProfiles);
        viewModel.Dispose();
    }

    public void Dispose() => _tempDir.Dispose();
}
