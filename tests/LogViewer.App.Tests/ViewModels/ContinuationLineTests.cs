using LogViewer.App.Tests.TestUtilities;
using LogViewer.Core.Structured;

namespace LogViewer.App.Tests.ViewModels;

/// <summary>Registers a custom format in the process-wide parser registry, hence the non-parallel collection.</summary>
[Collection(ParserRegistryCollection.Name)]
public sealed class ContinuationLineTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();

    private const string Log =
        "2026-09-23 10:00:00 INFO started\n" +
        "2026-09-23 10:00:01 ERROR request failed\n" +
        "System.InvalidOperationException: boom\n" +
        "   at Shop.Orders.Load()\n" +
        "2026-09-23 10:00:02 INFO recovered\n";

    [Fact]
    public void UnmatchedLines_JoinThePreviousEntry_InheritingItsLevel()
    {
        var format = new CustomLogFormat
        {
            Name = "Continuation test",
            Pattern = @"^(?<timestamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}) (?<level>[A-Z]+) (?<message>.*)$",
            JoinContinuationLines = true,
        };
        try
        {
            // Through settings: MainViewModel's constructor (re)registers the custom formats from them.
            var settings = new Core.Configuration.AppSettings { RestorePreviousSessionOnStartup = false, CustomLogFormats = [format] };
            var (main, _) = MainViewModelFactory.Create(settings);
            var doc = main.OpenPath(_tempDir.CreateFile("c.log", Log));
            TestDispatcher.SpinUntil(() => doc.Lines.Count >= 5);

            doc.StructuredFormatId = format.Id;
            doc.IsStructuredView = true;
            TestDispatcher.SpinUntil(() => doc.Lines.Count(l => l.Structured is not null) == 3);

            var exceptionLine = doc.Lines.Single(l => l.LineNumber == 3);
            var frame = doc.Lines.Single(l => l.LineNumber == 4);
            Assert.Equal(2, exceptionLine.ContinuationOf);
            Assert.Equal("Error", frame.InheritedLevel);
            Assert.Equal(LogLevelSeverity.Rank("Error"), frame.SeverityRank);
            Assert.Null(doc.Lines.Single(l => l.LineNumber == 5).ContinuationOf);

            doc.SelectedLine = doc.Lines.Single(l => l.LineNumber == 2);
            Assert.Contains("System.InvalidOperationException: boom", doc.SelectedEntryContinuation);
            Assert.Contains("at Shop.Orders.Load()", doc.SelectedEntryContinuation);

            doc.SelectedLine = doc.Lines.Single(l => l.LineNumber == 1);
            Assert.Null(doc.SelectedEntryContinuation);
            main.Dispose();
        }
        finally
        {
            LogLineParsers.SetCustomFormats([]);
        }
    }

    public void Dispose() => _tempDir.Dispose();
}
