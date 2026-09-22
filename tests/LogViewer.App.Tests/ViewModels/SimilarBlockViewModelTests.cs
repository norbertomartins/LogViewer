using LogViewer.App.Services;
using LogViewer.App.Tests.TestUtilities;
using LogViewer.App.ViewModels;
using LogViewer.Core.BlockDiff;
using NSubstitute;

namespace LogViewer.App.Tests.ViewModels;

public sealed class SimilarBlockViewModelTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();

    // Two lines sharing TraceId "t1" (a known correlation-field name), so CorrelationKeySelector
    // auto-suggests "TraceId" and a block built around either line picks up both.
    private const string Line1 = """{"@t":"2026-02-15T09:00:00.000Z","@mt":"start {TraceId}","@l":"Information","TraceId":"t1"}""";
    private const string Line2 = """{"@t":"2026-02-15T09:00:00.500Z","@mt":"end {TraceId}","@l":"Information","TraceId":"t1"}""";

    // LogLineParsers.Detect requires at least 2 sample lines to auto-detect a structured format, so
    // every source/target file below uses both lines even where a test only cares about the first.
    private const string BothLines = Line1 + "\n" + Line2 + "\n";

    [Fact]
    public void Constructor_ExcludesTheSourceDocument_AndSuggestsTheCorrelationField()
    {
        var (mainViewModel, _) = MainViewModelFactory.Create();
        var source = mainViewModel.OpenPath(_tempDir.CreateFile("source.clef", BothLines));
        SpinUntil(() => source.Lines.Count >= 2);
        var other = mainViewModel.OpenPath(_tempDir.CreateFile("other.clef", BothLines));
        SpinUntil(() => other.Lines.Count >= 1);

        var anchor = source.Lines[0];
        var viewModel = new SimilarBlockViewModel(
            source, anchor, [source, other], Substitute.For<ISimilarBlockFinder>(), Substitute.For<IDialogService>());

        var target = Assert.Single(viewModel.OpenTargetDocuments);
        Assert.Same(other, target);
        Assert.Equal("TraceId", viewModel.SuggestedCorrelationFields[0]);
        Assert.Equal("TraceId", viewModel.SelectedCorrelationField);
        Assert.Contains($"line {anchor.LineNumber}", viewModel.SourceDescription);

        mainViewModel.Dispose();
    }

    [Fact]
    public void BrowseTarget_SetsThePathFromTheDialog_AndClearsAnySelectedDocument()
    {
        var (mainViewModel, _) = MainViewModelFactory.Create();
        var source = mainViewModel.OpenPath(_tempDir.CreateFile("source.clef", BothLines));
        SpinUntil(() => source.Lines.Count >= 1);

        var dialogs = Substitute.For<IDialogService>();
        dialogs.ShowOpenFileDialog().Returns((IReadOnlyList<string>)["C:\\other.log"]);
        var viewModel = new SimilarBlockViewModel(source, source.Lines[0], [], Substitute.For<ISimilarBlockFinder>(), dialogs)
        {
            SelectedTargetDocument = source,
        };

        viewModel.BrowseTargetCommand.Execute(null);

        Assert.Equal("C:\\other.log", viewModel.BrowsedTargetPath);
        Assert.Null(viewModel.SelectedTargetDocument);

        mainViewModel.Dispose();
    }

    [Fact]
    public async Task FindAsync_WithNoTargetChosen_ShowsAChooseTargetStatus_AndFindsNothing()
    {
        var (mainViewModel, _) = MainViewModelFactory.Create();
        var source = mainViewModel.OpenPath(_tempDir.CreateFile("source.clef", BothLines));
        SpinUntil(() => source.Lines.Count >= 1);

        var viewModel = new SimilarBlockViewModel(source, source.Lines[0], [], Substitute.For<ISimilarBlockFinder>(), Substitute.For<IDialogService>());

        await viewModel.FindCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.Candidates);
        Assert.False(viewModel.IsSearching);

        mainViewModel.Dispose();
    }

    [Fact]
    public async Task FindAsync_WithAMatch_PopulatesCandidatesAndDiffEntries_ForTheTopScoredBlock()
    {
        var (mainViewModel, _) = MainViewModelFactory.Create();
        var source = mainViewModel.OpenPath(_tempDir.CreateFile("source.clef", BothLines));
        SpinUntil(() => source.Lines.Count >= 2);

        var targetPath = _tempDir.CreateFile("target.clef", Line1 + "\n" + Line2 + "\n");
        var other = mainViewModel.OpenPath(targetPath);
        SpinUntil(() => other.Lines.Count >= 2);

        var matchedBlock = LogBlockExtractor.ExtractByCorrelation(other.StructuredLines, "TraceId", "t1", other.Title);
        var finder = Substitute.For<ISimilarBlockFinder>();
        finder.FindBestMatchesAsync(Arg.Any<LogBlock>(), targetPath, Arg.Any<BlockDetectionOptions>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ScoredBlock>>([new ScoredBlock(matchedBlock, 0.95)]));

        var viewModel = new SimilarBlockViewModel(source, source.Lines[0], [source, other], finder, Substitute.For<IDialogService>())
        {
            SelectedTargetDocument = other,
        };

        await viewModel.FindCommand.ExecuteAsync(null);

        var candidate = Assert.Single(viewModel.Candidates);
        Assert.Equal(0.95, candidate.Score);
        Assert.Same(candidate, viewModel.SelectedCandidate);
        // Identical blocks on both sides align as two exact matches — auto-populated the moment the
        // first candidate is selected, no separate user action needed.
        Assert.Equal(2, viewModel.DiffEntries.Count);

        mainViewModel.Dispose();
    }

    [Fact]
    public void JumpToLeft_NavigatesTheSourceDocumentToThatLine()
    {
        var (mainViewModel, _) = MainViewModelFactory.Create();
        var source = mainViewModel.OpenPath(_tempDir.CreateFile("source.clef", BothLines));
        SpinUntil(() => source.Lines.Count >= 2);

        var viewModel = new SimilarBlockViewModel(source, source.Lines[0], [], Substitute.For<ISimilarBlockFinder>(), Substitute.For<IDialogService>());
        var entry = new DiffEntry(DiffLineKind.Common, new LogBlockLine(source.Lines[1].LineNumber, "sig", source.Lines[1].Structured!), null, false);

        viewModel.JumpToLeftCommand.Execute(entry);

        Assert.Equal(source.Lines[1].LineNumber, source.SelectedLine?.LineNumber);

        mainViewModel.Dispose();
    }

    [Fact]
    public void JumpToRight_WithNoTargetDocumentOpen_ShowsATargetNotOpenStatus()
    {
        var (mainViewModel, _) = MainViewModelFactory.Create();
        var source = mainViewModel.OpenPath(_tempDir.CreateFile("source.clef", BothLines));
        SpinUntil(() => source.Lines.Count >= 1);

        var viewModel = new SimilarBlockViewModel(source, source.Lines[0], [], Substitute.For<ISimilarBlockFinder>(), Substitute.For<IDialogService>())
        {
            BrowsedTargetPath = "C:\\some-file.log", // target came from Browse, not an open document
        };
        var entry = new DiffEntry(DiffLineKind.Common, null, new LogBlockLine(1, "sig", source.Lines[0].Structured!), false);

        viewModel.JumpToRightCommand.Execute(entry);

        Assert.NotNull(viewModel.StatusMessage);

        mainViewModel.Dispose();
    }

    [Fact]
    public void JumpToRight_WithTheTargetDocumentOpen_NavigatesIt()
    {
        var (mainViewModel, _) = MainViewModelFactory.Create();
        var source = mainViewModel.OpenPath(_tempDir.CreateFile("source.clef", BothLines));
        SpinUntil(() => source.Lines.Count >= 1);
        var other = mainViewModel.OpenPath(_tempDir.CreateFile("other.clef", Line1 + "\n" + Line2 + "\n"));
        SpinUntil(() => other.Lines.Count >= 2);

        var viewModel = new SimilarBlockViewModel(source, source.Lines[0], [source, other], Substitute.For<ISimilarBlockFinder>(), Substitute.For<IDialogService>())
        {
            SelectedTargetDocument = other,
        };
        var entry = new DiffEntry(DiffLineKind.Common, null, new LogBlockLine(other.Lines[1].LineNumber, "sig", other.Lines[1].Structured!), false);

        viewModel.JumpToRightCommand.Execute(entry);

        Assert.Equal(other.Lines[1].LineNumber, other.SelectedLine?.LineNumber);

        mainViewModel.Dispose();
    }

    public void Dispose() => _tempDir.Dispose();

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
}
