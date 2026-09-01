using System.IO;
using LogViewer.App.Services;
using LogViewer.App.Tests.TestUtilities;
using LogViewer.App.ViewModels;
using LogViewer.Core.Configuration;
using LogViewer.Core.EventLogging;
using NSubstitute;

namespace LogViewer.App.Tests.ViewModels;

public sealed class MainViewModelTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();

    [Fact]
    public void OpenPath_AddsDocumentAndRecordsAsMostRecentFile()
    {
        var filePath = _tempDir.CreateFile("a.log", "hello\n");
        var (viewModel, settings) = MainViewModelFactory.Create();

        var document = viewModel.OpenPath(filePath);

        Assert.Single(viewModel.Documents);
        Assert.Same(document, viewModel.ActiveDocument);
        Assert.Equal(Path.GetFullPath(filePath), viewModel.RecentFiles[0].Path, ignoreCase: true);

        viewModel.Dispose();
    }

    [Fact]
    public void OpenPath_CalledTwiceForSamePath_ActivatesExistingDocumentInsteadOfDuplicating()
    {
        var filePath = _tempDir.CreateFile("a.log", "hello\n");
        var (viewModel, _) = MainViewModelFactory.Create();

        var first = viewModel.OpenPath(filePath);
        var second = viewModel.OpenPath(filePath);

        Assert.Same(first, second);
        Assert.Single(viewModel.Documents);

        viewModel.Dispose();
    }

    [Fact]
    public void RecentFiles_OnlyIncludesFileKindSources_NotDirectoryWatchesOrEventLogs()
    {
        var filePath = _tempDir.CreateFile("a.log");
        var (viewModel, _) = MainViewModelFactory.Create();

        viewModel.OpenPath(filePath);
        viewModel.OpenDirectoryWatch(_tempDir.DirectoryPath, "*.log", autoSwitchToLatestFile: true);
        viewModel.OpenEventLog("Application", Array.Empty<EventLogFilterRule>());

        var recent = viewModel.RecentFiles;

        Assert.Single(recent);
        Assert.Equal(TailSourceKind.File, recent[0].Kind);

        viewModel.Dispose();
    }

    [Fact]
    public void ShowCommandPalette_ExecutesTheChosenCommand()
    {
        var dialogs = Substitute.For<IDialogService>();
        var (viewModel, _) = MainViewModelFactory.Create(dialogService: dialogs);
        dialogs.ShowCommandPalette(Arg.Any<IReadOnlyList<PaletteCommand>>())
            .Returns(ci => ((IReadOnlyList<PaletteCommand>)ci[0]).First(c => c.Title == "Window Mode: MDI"));

        viewModel.ShowCommandPaletteCommand.Execute(null);

        Assert.Equal(LogViewer.Core.Configuration.WindowModeKind.Mdi, viewModel.Host.Mode);
        viewModel.Dispose();
    }

    [Fact]
    public void BuildPaletteCommands_IncludesGoToEntryPerOpenDocument()
    {
        var a = _tempDir.CreateFile("first.log", "x\n");
        var b = _tempDir.CreateFile("second.log", "y\n");
        var (viewModel, _) = MainViewModelFactory.Create();
        viewModel.OpenPath(a);
        viewModel.OpenPath(b);

        var commands = viewModel.BuildPaletteCommands();

        Assert.Contains(commands, c => c.Category == "Document" && c.Title.Contains("first.log"));
        Assert.Contains(commands, c => c.Category == "Document" && c.Title.Contains("second.log"));
        Assert.Contains(commands, c => c.Title == "Open File…");
        viewModel.Dispose();
    }

    [Fact]
    public void OpenMergedFiles_AddsOneDocument_RecordedAsMergedKind_NotInRecentFiles()
    {
        var a = _tempDir.CreateFile("a.log", "2026-01-02 10:00:01 one\n");
        var b = _tempDir.CreateFile("b.log", "2026-01-02 10:00:02 two\n");
        var (viewModel, _) = MainViewModelFactory.Create();

        var document = viewModel.OpenMergedFiles([a, b]);

        Assert.Single(viewModel.Documents);
        Assert.Same(document, viewModel.ActiveDocument);
        Assert.Equal(TailSourceKind.MergedFiles, document.Kind);
        Assert.Empty(viewModel.RecentFiles); // merged sources are not "recent files"

        viewModel.Dispose();
    }

    [Fact]
    public void OpenMergedFiles_WithStructuredFiles_EnablesStructuredViewWithDetectedFormat()
    {
        var clef = "{\"@t\":\"2026-01-02T10:00:01Z\",\"@mt\":\"a {X}\",\"X\":1}\n{\"@t\":\"2026-01-02T10:00:03Z\",\"@mt\":\"b {X}\",\"X\":2}\n";
        var a = _tempDir.CreateFile("a.clef", clef);
        var b = _tempDir.CreateFile("b.clef", clef);
        var (viewModel, _) = MainViewModelFactory.Create();

        var document = viewModel.OpenMergedFiles([a, b]);

        Assert.True(document.IsStructuredView);
        Assert.Equal("serilog", document.StructuredFormatId);

        viewModel.Dispose();
    }

    [Fact]
    public void OpenRemoteEndpoint_Http_AddsRemoteHttpDocument_NotInRecentFiles_DedupsByUrl()
    {
        var (viewModel, _) = MainViewModelFactory.Create();

        var first = viewModel.OpenRemoteEndpoint("https://logs.invalid/tail", "Poll", []);
        var second = viewModel.OpenRemoteEndpoint("https://logs.invalid/tail", "Poll", []);

        Assert.Same(first, second);
        Assert.Single(viewModel.Documents);
        Assert.Equal(TailSourceKind.RemoteHttp, first.Kind);
        Assert.Empty(viewModel.RecentFiles);

        viewModel.Dispose();
    }

    [Fact]
    public void OpenRemoteEndpoint_WebSocketScheme_CreatesWebSocketDocument()
    {
        var (viewModel, _) = MainViewModelFactory.Create();

        var doc = viewModel.OpenRemoteEndpoint("wss://logs.invalid/stream", "Auto", []);

        Assert.Equal(TailSourceKind.RemoteWebSocket, doc.Kind);

        viewModel.Dispose();
    }

    [Fact]
    public void OpenProcessTail_AddsProcessDocument_DedupsByCommand_NotInRecentFiles()
    {
        var (viewModel, _) = MainViewModelFactory.Create();

        var first = viewModel.OpenProcessTail("cmd.exe", "/c ver", restartOnExit: false);
        var second = viewModel.OpenProcessTail("cmd.exe", "/c ver", restartOnExit: false);

        Assert.Same(first, second);
        Assert.Equal(TailSourceKind.Process, first.Kind);
        Assert.Empty(viewModel.RecentFiles);

        viewModel.Dispose();
    }

    [Fact]
    public void OpenEtwTail_AddsEtwDocument()
    {
        var (viewModel, _) = MainViewModelFactory.Create();

        var doc = viewModel.OpenEtwTail("Microsoft-Windows-DotNETRuntime", 4);

        Assert.Equal(TailSourceKind.Etw, doc.Kind);

        viewModel.Dispose();
    }

    [Fact]
    public void OpenMergedFiles_SamePathsDifferentOrder_ActivatesExistingDocument()
    {
        var a = _tempDir.CreateFile("a.log", "x\n");
        var b = _tempDir.CreateFile("b.log", "y\n");
        var (viewModel, _) = MainViewModelFactory.Create();

        var first = viewModel.OpenMergedFiles([a, b]);
        var second = viewModel.OpenMergedFiles([b, a]);

        Assert.Same(first, second);
        Assert.Single(viewModel.Documents);

        viewModel.Dispose();
    }

    [Fact]
    public void OpenDirectoryWatch_CalledTwiceForSameDirectoryAndPattern_ActivatesExistingDocument()
    {
        var (viewModel, _) = MainViewModelFactory.Create();

        var first = viewModel.OpenDirectoryWatch(_tempDir.DirectoryPath, "*.log", autoSwitchToLatestFile: true);
        var second = viewModel.OpenDirectoryWatch(_tempDir.DirectoryPath, "*.log", autoSwitchToLatestFile: true);

        Assert.Same(first, second);
        Assert.Single(viewModel.Documents);

        viewModel.Dispose();
    }

    [Fact]
    public void OpenEventLog_CalledTwiceForSameChannel_ActivatesExistingDocument()
    {
        var (viewModel, _) = MainViewModelFactory.Create();

        var first = viewModel.OpenEventLog("Application", Array.Empty<EventLogFilterRule>());
        var second = viewModel.OpenEventLog("Application", Array.Empty<EventLogFilterRule>());

        Assert.Same(first, second);
        Assert.Single(viewModel.Documents);

        viewModel.Dispose();
    }

    [Fact]
    public void CloseDocument_WhenClosingActiveDocument_ActivatesNextRemainingDocument()
    {
        var fileA = _tempDir.CreateFile("a.log");
        var fileB = _tempDir.CreateFile("b.log");
        var (viewModel, _) = MainViewModelFactory.Create();

        var docA = viewModel.OpenPath(fileA);
        var docB = viewModel.OpenPath(fileB);
        Assert.Same(docB, viewModel.ActiveDocument);

        viewModel.CloseDocumentCommand.Execute(docB);

        Assert.Single(viewModel.Documents);
        Assert.Same(docA, viewModel.ActiveDocument);

        viewModel.Dispose();
    }

    [Fact]
    public void CloseDocument_WhenNoDocumentsRemain_ActiveDocumentBecomesNull()
    {
        var filePath = _tempDir.CreateFile("a.log");
        var (viewModel, _) = MainViewModelFactory.Create();

        var doc = viewModel.OpenPath(filePath);
        viewModel.CloseDocumentCommand.Execute(doc);

        Assert.Empty(viewModel.Documents);
        Assert.Null(viewModel.ActiveDocument);

        viewModel.Dispose();
    }

    [Fact]
    public void SwitchWindowMode_WithValidModeName_UpdatesHostMode()
    {
        var (viewModel, _) = MainViewModelFactory.Create();

        viewModel.SwitchWindowModeCommand.Execute("Mdi");

        Assert.Equal(WindowModeKind.Mdi, viewModel.Host.Mode);

        viewModel.Dispose();
    }

    [Fact]
    public void SwitchWindowMode_WithUnrecognizedModeName_LeavesModeUnchanged()
    {
        var (viewModel, _) = MainViewModelFactory.Create();
        var originalMode = viewModel.Host.Mode;

        viewModel.SwitchWindowModeCommand.Execute("NotARealMode");

        Assert.Equal(originalMode, viewModel.Host.Mode);

        viewModel.Dispose();
    }

    [Fact]
    public void OpenFile_WhenDialogIsCancelled_DoesNotAddADocument()
    {
        var dialogs = Substitute.For<IDialogService>();
        dialogs.ShowOpenFileDialog().Returns((IReadOnlyList<string>?)null);
        var (viewModel, _) = MainViewModelFactory.Create(dialogService: dialogs);

        viewModel.OpenFileCommand.Execute(null);

        Assert.Empty(viewModel.Documents);

        viewModel.Dispose();
    }

    [Fact]
    public void OpenFile_WhenDialogReturnsPaths_OpensEachOne()
    {
        var fileA = _tempDir.CreateFile("a.log");
        var fileB = _tempDir.CreateFile("b.log");
        var dialogs = Substitute.For<IDialogService>();
        dialogs.ShowOpenFileDialog().Returns(new[] { fileA, fileB });
        var (viewModel, _) = MainViewModelFactory.Create(dialogService: dialogs);

        viewModel.OpenFileCommand.Execute(null);

        Assert.Equal(2, viewModel.Documents.Count);

        viewModel.Dispose();
    }

    [Fact]
    public void OpenDirectoryWatch_PromptsViaDialogService_UsingInitialDirectoryPath()
    {
        var dialogs = Substitute.For<IDialogService>();
        dialogs.ShowOpenDirectoryWatchDialog(_tempDir.DirectoryPath)
            .Returns(new DirectoryWatchSelection(_tempDir.DirectoryPath, "*.txt", true));
        var (viewModel, _) = MainViewModelFactory.Create(dialogService: dialogs);

        viewModel.PromptOpenDirectoryWatch(_tempDir.DirectoryPath);

        Assert.Single(viewModel.Documents);
        dialogs.Received(1).ShowOpenDirectoryWatchDialog(_tempDir.DirectoryPath);

        viewModel.Dispose();
    }

    [Fact]
    public void EditHighlightPresets_WhenDialogSavesChanges_RefreshesToggleList()
    {
        var preset = new LogViewer.Core.Highlighting.HighlightPreset { Name = "P1", IsEnabled = true };
        var settings = new AppSettings { RestorePreviousSessionOnStartup = false };
        settings.HighlightPresets.Add(preset);

        var dialogs = Substitute.For<IDialogService>();
        dialogs.ShowHighlightPresetEditor(settings.HighlightPresets).Returns(true);
        var (viewModel, _) = MainViewModelFactory.Create(settings, dialogs);

        viewModel.EditHighlightPresetsCommand.Execute(null);

        Assert.Single(viewModel.HighlightPresetToggles);
        Assert.Equal(preset.Name, viewModel.HighlightPresetToggles[0].Name);

        viewModel.Dispose();
    }

    [Fact]
    public void EditHighlightPresets_WhenDialogIsCancelled_LeavesToggleListUnchanged()
    {
        var settings = new AppSettings { RestorePreviousSessionOnStartup = false };
        settings.HighlightPresets.Add(new LogViewer.Core.Highlighting.HighlightPreset { Name = "P1" });

        var dialogs = Substitute.For<IDialogService>();
        dialogs.ShowHighlightPresetEditor(settings.HighlightPresets).Returns(false);
        var (viewModel, _) = MainViewModelFactory.Create(settings, dialogs);

        // Constructor already populated toggles from the starting presets; cancelling the dialog
        // should not trigger a second refresh (nothing to assert differently, but must not throw).
        viewModel.EditHighlightPresetsCommand.Execute(null);

        Assert.Single(viewModel.HighlightPresetToggles);

        viewModel.Dispose();
    }

    public void Dispose() => _tempDir.Dispose();
}
