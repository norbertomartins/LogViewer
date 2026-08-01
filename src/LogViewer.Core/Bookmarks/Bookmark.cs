namespace LogViewer.Core.Bookmarks;

public sealed record Bookmark(Guid Id, long LineNumber, DateTimeOffset CreatedUtc, string? Note);
