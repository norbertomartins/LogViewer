using LogViewer.App.Services;
using LogViewer.App.Tests.TestUtilities;
using LogViewer.App.ViewModels;
using LogViewer.Core.BlockDiff;
using NSubstitute;

namespace LogViewer.App.Tests.ViewModels;

public sealed class CompareFilesViewModelTests : IDisposable
{
    private readonly TempDirectoryFixture _tempDir = new();

    [Fact]
    public async Task CompareAsync_AlignsTwoFiles_ReportingCommonAndOnlyInOneSide()
    {
        var pathA = _tempDir.CreateFile("a.clef", string.Join('\n',
        [
            Clef("2026-01-01T00:00:00Z", "start"),
            Clef("2026-01-01T00:00:01Z", "shared step"),
            Clef("2026-01-01T00:00:02Z", "only in A"),
        ]) + "\n");

        var pathB = _tempDir.CreateFile("b.clef", string.Join('\n',
        [
            Clef("2026-01-01T00:00:00Z", "start"),
            Clef("2026-01-01T00:00:01Z", "shared step"),
            Clef("2026-01-01T00:00:02Z", "only in B"),
        ]) + "\n");

        var openPath = Substitute.For<Action<string, long>>();
        var vm = new CompareFilesViewModel(Substitute.For<IDialogService>(), openPath)
        {
            PathA = pathA,
            PathB = pathB,
        };

        await vm.CompareCommand.ExecuteAsync(null);

        Assert.Equal(4, vm.DiffEntries.Count);
        Assert.Equal(2, vm.DiffEntries.Count(e => e.Kind == DiffLineKind.Common));
        Assert.Contains(vm.DiffEntries, e => e.Kind == DiffLineKind.OnlyInLeft);
        Assert.Contains(vm.DiffEntries, e => e.Kind == DiffLineKind.OnlyInRight);
    }

    [Fact]
    public void JumpToLeft_InvokesOpenPathWithFileAAndLineNumber()
    {
        var openPath = Substitute.For<Action<string, long>>();
        var vm = new CompareFilesViewModel(Substitute.For<IDialogService>(), openPath) { PathA = "a.log" };

        var leftEvent = new LogViewer.Core.Structured.StructuredLogEvent(null, null, null, "x", null, new Dictionary<string, string>());
        var entry = new DiffEntry(DiffLineKind.Common, new LogBlockLine(7, "sig", leftEvent), null, false);
        vm.JumpToLeftCommand.Execute(entry);

        openPath.Received(1).Invoke("a.log", 7);
    }

    private static string Clef(string timestamp, string message) => $"{{\"@t\":\"{timestamp}\",\"@m\":\"{message}\"}}";

    public void Dispose() => _tempDir.Dispose();
}
