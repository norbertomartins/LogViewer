using System.IO;
using LogViewer.App.Services;
using LogViewer.App.Tests.TestUtilities;
using NSubstitute;

namespace LogViewer.App.Tests.ViewModels;

public sealed class IncidentReportExportTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();

    [Theory]
    [InlineData("report.md", "# Incident report — app.log", "► 2  ERROR boom")]
    [InlineData("report.html", "<!DOCTYPE html>", "<span class=\"l m\"><span class=\"n\">2  </span>ERROR boom")]
    public async Task Export_WritesTheBookmarksAndNotes_InTheFormatOfTheChosenExtension(string fileName, string expectedStart, string expectedLine)
    {
        var target = Path.Combine(_tempDir.DirectoryPath, fileName);
        var dialogs = Substitute.For<IDialogService>();
        dialogs.ShowSaveIncidentReportDialog(Arg.Any<string>()).Returns(target);
        var path = _tempDir.CreateFile("app.log", "INFO start\nERROR boom\nINFO a\nINFO b\nINFO c\nINFO d\nWARN slow\n");
        var (main, _) = MainViewModelFactory.Create(dialogService: dialogs, annotationStore: new InMemoryAnnotationStore());
        var doc = main.OpenPath(path);
        SpinUntil(() => doc.Lines.Count >= 7);
        doc.EnsureBookmarked(2);
        doc.SetNote(doc.Lines[6], "slow disk");

        await main.ExportIncidentReportAsync(doc);

        var content = File.ReadAllText(target);
        Assert.StartsWith(expectedStart, content);
        Assert.Contains(expectedLine, content);
        Assert.Contains("slow disk", content);
        Assert.Contains("app.log", dialogs.ReceivedCalls().Single().GetArguments()[0] as string);
        Assert.Contains(fileName, main.StatusMessage);
        main.Dispose();
    }

    [Fact]
    public async Task Cancelling_TheSaveDialog_WritesNothing()
    {
        var dialogs = Substitute.For<IDialogService>();
        dialogs.ShowSaveIncidentReportDialog(Arg.Any<string>()).Returns((string?)null);
        var path = _tempDir.CreateFile("app.log", "INFO start\n");
        var (main, _) = MainViewModelFactory.Create(dialogService: dialogs, annotationStore: new InMemoryAnnotationStore());
        var doc = main.OpenPath(path);
        SpinUntil(() => doc.Lines.Count >= 1);

        await main.ExportIncidentReportAsync(doc);

        Assert.Single(Directory.GetFiles(_tempDir.DirectoryPath));
        main.Dispose();
    }

    private static void SpinUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                () => { }, System.Windows.Threading.DispatcherPriority.Background);
            Thread.Sleep(25);
        }
    }

    public void Dispose() => _tempDir.Dispose();
}
