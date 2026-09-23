using LogViewer.Core.Annotations;

namespace LogViewer.Core.Tests.Annotations;

public sealed class LineAnnotationStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "lv-annotations-" + Guid.NewGuid().ToString("N"));

    private string StorePath => Path.Combine(_directory, "annotations.json");

    [Fact]
    public void Set_PersistsAcrossInstances_KeyedCaseInsensitively()
    {
        var note = new LineAnnotation(12, LineAnnotationStore.HashText("ERROR boom"), "root cause", DateTimeOffset.UnixEpoch);
        new LineAnnotationStore(StorePath).Set(@"C:\Logs\App.log", [note]);

        var reloaded = new LineAnnotationStore(StorePath).Get(@"c:\logs\app.log");

        Assert.Equal([note], reloaded);
    }

    [Fact]
    public void Set_WithNoNotes_RemovesTheFile()
    {
        var store = new LineAnnotationStore(StorePath);
        store.Set("a.log", [new LineAnnotation(1, "h", "n", DateTimeOffset.UnixEpoch)]);
        store.Set("a.log", []);

        Assert.Empty(new LineAnnotationStore(StorePath).Get("a.log"));
    }

    [Fact]
    public void Get_WithACorruptFile_ReturnsNoNotes()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(StorePath, "{ not json");

        Assert.Empty(new LineAnnotationStore(StorePath).Get("a.log"));
    }

    [Fact]
    public void HashText_IsStableAndTextSensitive()
    {
        Assert.Equal(LineAnnotationStore.HashText("same"), LineAnnotationStore.HashText("same"));
        Assert.NotEqual(LineAnnotationStore.HashText("same"), LineAnnotationStore.HashText("same "));
        Assert.Equal(16, LineAnnotationStore.HashText("x").Length);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Best-effort (includes "nothing was written").
        }
    }
}
