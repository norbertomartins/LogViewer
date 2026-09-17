using LogViewer.App.Tests.TestUtilities;
using LogViewer.App.ViewModels;
using LogViewer.Core.Analysis;
using NSubstitute;

namespace LogViewer.App.Tests.ViewModels;

public sealed class DocumentStatsViewModelTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();

    [Fact]
    public void Construction_ComputesErrorAndWarningCountsFromBufferedLines()
    {
        var lines = string.Join('\n',
        [
            "2026-02-15 09:00:00.000 [INFO] all good",
            "2026-02-15 09:00:01.000 [WARN] getting worried",
            "2026-02-15 09:00:02.000 [ERROR] boom",
            "2026-02-15 09:00:03.000 [ERROR] boom again",
        ]) + "\n";

        var (viewModel, _) = MainViewModelFactory.Create();
        var doc = viewModel.OpenPath(_tempDir.CreateFile("p.log", lines));
        SpinUntil(() => doc.Lines.Count >= 4);

        var analyzer = Substitute.For<IPatternFrequencyAnalyzer>();
        analyzer.AnalyzeBySignatureAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PatternFrequencyEntry>>([]));

        using var stats = new DocumentStatsViewModel(doc, analyzer);

        Assert.Equal(2, stats.ErrorCount);
        Assert.Equal(1, stats.WarningCount);

        viewModel.Dispose();
    }

    [Fact]
    public async Task AnalyzeAsync_PopulatesTopPatterns_FromAnalyzerResult()
    {
        var (viewModel, _) = MainViewModelFactory.Create();
        var doc = viewModel.OpenPath(_tempDir.CreateFile("p.log", "2026-02-15 09:00:00.000 [INFO] hello\n"));
        SpinUntil(() => doc.Lines.Count >= 1);

        var entry = new PatternFrequencyEntry(
            Signature: "sig", Level: "Information", SampleMessage: "hello",
            Count: 5, FirstLineNumber: 1, FirstTimestamp: null, LastLineNumber: 1, LastTimestamp: null, SampleLineNumbers: [1]);

        var analyzer = Substitute.For<IPatternFrequencyAnalyzer>();
        analyzer.AnalyzeBySignatureAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PatternFrequencyEntry>>([entry]));

        using var stats = new DocumentStatsViewModel(doc, analyzer);
        await stats.AnalyzeCommand.ExecuteAsync(null);

        Assert.Single(stats.TopPatterns);
        Assert.Equal("hello", stats.TopPatterns[0].SampleMessage);

        viewModel.Dispose();
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
