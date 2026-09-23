namespace LogViewer.Core.Indexing;

/// <summary>
/// Process-wide cache of <see cref="FileLineIndex"/> instances by full path, so repeated queries against the same
/// large file (e.g. an MCP agent asking for several time ranges in a row) only index it once and afterwards just
/// scan appended bytes. Bounded to <see cref="Capacity"/> files, least recently used evicted first.
/// </summary>
public static class FileLineIndexCache
{
    public const int Capacity = 16;

    private static readonly object Sync = new();
    private static readonly LinkedList<FileLineIndex> Lru = new();

    /// <summary>Returns an up-to-date index for <paramref name="path"/> (created or incrementally updated).</summary>
    public static async Task<FileLineIndex> GetAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        FileLineIndex index;
        lock (Sync)
        {
            var node = Lru.First;
            while (node is not null && !string.Equals(node.Value.Path, fullPath, StringComparison.OrdinalIgnoreCase))
            {
                node = node.Next;
            }

            if (node is null)
            {
                node = new LinkedListNode<FileLineIndex>(new FileLineIndex(fullPath));
                if (Lru.Count >= Capacity)
                {
                    Lru.RemoveLast();
                }
            }
            else
            {
                Lru.Remove(node);
            }

            Lru.AddFirst(node);
            index = node.Value;
        }

        await index.UpdateAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        return index;
    }
}
