using LogViewer.Core.Indexing;
using LogViewer.Core.Tests.TestUtilities;

namespace LogViewer.Core.Tests.Indexing;

/// <summary>Changes the process-wide persistence settings, so it must not run alongside other index tests.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class IndexPersistenceCollection
{
    public const string Name = "FileLineIndex persistence";
}

[Collection(IndexPersistenceCollection.Name)]
public sealed class FileLineIndexPersistenceTests : IDisposable
{
    private readonly string _cacheDir = Path.Combine(Path.GetTempPath(), "LogViewerIndexCache_" + Guid.NewGuid().ToString("N"));
    private readonly string? _previousDirectory = FileLineIndex.PersistDirectory;
    private readonly long _previousMinSize = FileLineIndex.PersistMinFileSize;

    public FileLineIndexPersistenceTests()
    {
        FileLineIndex.PersistDirectory = _cacheDir;
        FileLineIndex.PersistMinFileSize = 1; // persist even tiny test files
    }

    private static string Lines(int from, int count) => string.Concat(Enumerable.Range(from, count).Select(i => $"line {i}\n"));

    [Fact]
    public async Task ReopenedIndex_LoadsFromCache_AndOnlyScansAppendedBytes()
    {
        using var file = new TempFileFixture();
        file.WriteAllText(Lines(1, 3000));
        var first = new FileLineIndex(file.FilePath, stride: 100);
        await first.UpdateAsync();
        Assert.False(first.LoadedFromCache);
        Assert.Single(Directory.GetFiles(_cacheDir, "*.lvidx"));

        file.AppendText(Lines(3001, 10));
        var reopened = new FileLineIndex(file.FilePath, stride: 100);
        await reopened.UpdateAsync();

        Assert.True(reopened.LoadedFromCache);
        Assert.Equal(3010, reopened.LineCount);
        Assert.Equal("line 1234", reopened.ReadLines(1234, 1).Single().Text);
        Assert.Equal("line 3010", reopened.ReadLines(3010, 1).Single().Text);
    }

    [Fact]
    public async Task CacheIsIgnored_WhenTheFileWasRewritten()
    {
        using var file = new TempFileFixture();
        file.WriteAllText(Lines(1, 2000));
        await new FileLineIndex(file.FilePath, stride: 100).UpdateAsync();

        // Same length, different content: a rotated/rewritten file must not reuse the old offsets.
        file.WriteAllText(Lines(1, 2000).Replace("line", "LINE"));
        var reopened = new FileLineIndex(file.FilePath, stride: 100);
        await reopened.UpdateAsync();

        Assert.False(reopened.LoadedFromCache);
        Assert.Equal("LINE 77", reopened.ReadLines(77, 1).Single().Text);
    }

    [Fact]
    public async Task CacheIsIgnored_ForADifferentStride_OrWhenDisabled()
    {
        using var file = new TempFileFixture();
        file.WriteAllText(Lines(1, 500));
        await new FileLineIndex(file.FilePath, stride: 50).UpdateAsync();

        var otherStride = new FileLineIndex(file.FilePath, stride: 64);
        await otherStride.UpdateAsync();
        Assert.False(otherStride.LoadedFromCache);

        FileLineIndex.PersistDirectory = null;
        var disabled = new FileLineIndex(file.FilePath, stride: 50);
        await disabled.UpdateAsync();
        Assert.False(disabled.LoadedFromCache);
    }

    public void Dispose()
    {
        FileLineIndex.PersistDirectory = _previousDirectory;
        FileLineIndex.PersistMinFileSize = _previousMinSize;
        try
        {
            Directory.Delete(_cacheDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
