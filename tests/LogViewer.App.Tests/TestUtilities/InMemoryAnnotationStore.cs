using LogViewer.Core.Annotations;

namespace LogViewer.App.Tests.TestUtilities;

/// <summary>An <see cref="ILineAnnotationStore"/> that never touches disk.</summary>
public sealed class InMemoryAnnotationStore : ILineAnnotationStore
{
    private readonly Dictionary<string, IReadOnlyList<LineAnnotation>> _entries = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<LineAnnotation> Get(string fileKey) => _entries.TryGetValue(fileKey, out var list) ? list : [];

    public void Set(string fileKey, IReadOnlyList<LineAnnotation> annotations)
    {
        if (annotations.Count == 0)
        {
            _entries.Remove(fileKey);
        }
        else
        {
            _entries[fileKey] = [.. annotations];
        }
    }
}
