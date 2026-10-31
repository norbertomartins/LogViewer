using System.IO;
using LogViewer.App.Tests.TestUtilities;
using LogViewer.Core.Configuration;

namespace LogViewer.App.Tests.ViewModels;

public sealed class DetailPanelStateTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();

    [Fact]
    public void NewDocument_StartsFromPersistedCollapsedAndHoverSettings()
    {
        var settings = new AppSettings { RestorePreviousSessionOnStartup = false };
        settings.Layout.IsDetailPanelCollapsed = true;
        settings.Layout.DetailPanelHoverOpen = true;
        var (main, _) = MainViewModelFactory.Create(settings);

        var doc = main.OpenPath(_tempDir.CreateFile("a.log", "line\n"));

        Assert.True(doc.IsDetailPanelCollapsed);
        Assert.True(doc.DetailPanelHoverOpen);
        Assert.False(doc.IsDetailPanelExpanded);
        main.Dispose();
    }

    [Fact]
    public void TogglingCollapse_PersistsAndSyncsOtherDocuments()
    {
        var settings = new AppSettings { RestorePreviousSessionOnStartup = false };
        var (main, _) = MainViewModelFactory.Create(settings);
        var a = main.OpenPath(_tempDir.CreateFile("a.log", "line\n"));
        var b = main.OpenPath(_tempDir.CreateFile("b.log", "line\n"));
        Assert.False(settings.Layout.IsDetailPanelCollapsed);

        a.ToggleDetailPanelCollapsedCommand.Execute(null);

        Assert.True(settings.Layout.IsDetailPanelCollapsed);
        Assert.True(b.IsDetailPanelCollapsed);

        a.ToggleDetailPanelCollapsedCommand.Execute(null);

        Assert.False(settings.Layout.IsDetailPanelCollapsed);
        Assert.False(b.IsDetailPanelCollapsed);
        main.Dispose();
    }

    [Fact]
    public void HoverExpansion_IsTransient_AndDoesNotChangeSavedCollapsedState()
    {
        var settings = new AppSettings { RestorePreviousSessionOnStartup = false };
        var (main, _) = MainViewModelFactory.Create(settings);
        var doc = main.OpenPath(_tempDir.CreateFile("a.log", "line\n"));
        doc.IsDetailPanelCollapsed = true;

        doc.IsDetailPanelHoverExpanded = true;

        Assert.True(doc.IsDetailPanelExpanded);
        Assert.True(settings.Layout.IsDetailPanelCollapsed);

        doc.IsDetailPanelHoverExpanded = false;
        Assert.False(doc.IsDetailPanelExpanded);
        main.Dispose();
    }

    [Fact]
    public void MenuHoverOpenToggle_PersistsAndAppliesToOpenDocuments_AndClearsHoverExpansionWhenOff()
    {
        var settings = new AppSettings { RestorePreviousSessionOnStartup = false };
        var (main, _) = MainViewModelFactory.Create(settings);
        var doc = main.OpenPath(_tempDir.CreateFile("a.log", "line\n"));

        main.DetailPanelHoverOpen = true;

        Assert.True(settings.Layout.DetailPanelHoverOpen);
        Assert.True(doc.DetailPanelHoverOpen);

        doc.IsDetailPanelCollapsed = true;
        doc.IsDetailPanelHoverExpanded = true;
        main.DetailPanelHoverOpen = false;

        Assert.False(settings.Layout.DetailPanelHoverOpen);
        Assert.False(doc.IsDetailPanelHoverExpanded);
        main.Dispose();
    }

    [Fact]
    public void TabToolTip_ForAFile_ShowsFolderAndFileName()
    {
        var (main, _) = MainViewModelFactory.Create(new AppSettings { RestorePreviousSessionOnStartup = false });
        var path = _tempDir.CreateFile("app.log", "line\n");

        var doc = main.OpenPath(path);

        Assert.Contains(Path.GetDirectoryName(path)!, doc.TabToolTip);
        Assert.Contains("app.log", doc.TabToolTip);
        main.Dispose();
    }

    public void Dispose() => _tempDir.Dispose();
}
