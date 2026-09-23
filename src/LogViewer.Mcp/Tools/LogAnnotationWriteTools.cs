using System.ComponentModel;
using LogViewer.Core.Documents;
using LogViewer.Core.Indexing;
using ModelContextProtocol.Server;

namespace LogViewer.Mcp.Tools;

/// <summary><paramref name="LineText"/> is the line the bookmark/note landed on, so the agent can check it picked the
/// right one; <paramref name="Error"/> says why nothing was written.</summary>
public sealed record AnnotationWriteResult(bool Written, long LineNumber, string? LineText, string? Error);

/// <summary>The only MCP tools that change anything, and only the app's own bookmarks and notes — never a log file.
/// Registered only when the user turned on <see cref="Core.Configuration.McpServerSettings.AllowAnnotationWrites"/>.</summary>
[McpServerToolType]
public sealed class LogAnnotationWriteTools(IDocumentAnnotationWriter writer)
{
    /// <summary>Longest note the agent may write; notes are shown in a tooltip, not meant for whole reports.</summary>
    public const int MaxNoteLength = 500;

    /// <summary>Marks the notes the agent wrote, so the user can tell them from their own.</summary>
    public const string AgentNotePrefix = "[AI] ";

    [McpServerTool(Name = "logs_add_bookmark")]
    [Description(
        "Bookmarks a line in a log file that is open in LogViewer, so the user can jump to it (F2) — use it to point " +
        "the user at the lines your analysis relies on. Changes only the app's bookmarks, never the file. The file " +
        "must be open as a document (see logs_list_open_documents); line numbers are the ones the other tools return.")]
    public async Task<AnnotationWriteResult> AddBookmark(
        [Description("Full path of the open log file.")] string sourcePath,
        [Description("1-based line number.")] long lineNumber,
        CancellationToken cancellationToken)
    {
        var (text, error) = await ReadLineAsync(sourcePath, lineNumber, cancellationToken).ConfigureAwait(false);
        error ??= writer.AddBookmark(sourcePath, lineNumber);
        return new AnnotationWriteResult(error is null, lineNumber, text is null ? null : ResponseLimits.Truncate(text), error);
    }

    [McpServerTool(Name = "logs_add_note")]
    [Description(
        "Adds a short note to a line of a log file open in LogViewer (shown on the line and in its tooltip, prefixed " +
        "'[AI]'), e.g. 'first failure of the retry storm'. Added after any note the user already wrote rather than " +
        "replacing it. Changes only the app's notes, never the file; the file must be open as a single-file document.")]
    public async Task<AnnotationWriteResult> AddNote(
        [Description("Full path of the open log file.")] string sourcePath,
        [Description("1-based line number.")] long lineNumber,
        [Description("The note, one or two sentences (longer text is cut).")] string note,
        CancellationToken cancellationToken)
    {
        var (text, error) = await ReadLineAsync(sourcePath, lineNumber, cancellationToken).ConfigureAwait(false);
        var trimmed = note?.ReplaceLineEndings(" ").Trim() ?? string.Empty;
        if (error is null && trimmed.Length == 0)
        {
            error = "The note is empty.";
        }

        if (trimmed.Length > MaxNoteLength)
        {
            trimmed = trimmed[..MaxNoteLength] + "…";
        }

        error ??= writer.AddNote(sourcePath, lineNumber, text!, AgentNotePrefix + trimmed);
        return new AnnotationWriteResult(error is null, lineNumber, text is null ? null : ResponseLimits.Truncate(text), error);
    }

    private static async Task<(string? Text, string? Error)> ReadLineAsync(string sourcePath, long lineNumber, CancellationToken cancellationToken)
    {
        if (!File.Exists(sourcePath))
        {
            return (null, $"File not found: {sourcePath}");
        }

        var index = await FileLineIndexCache.GetAsync(sourcePath, cancellationToken).ConfigureAwait(false);
        if (lineNumber < 1 || lineNumber > index.LineCount)
        {
            return (null, $"Line {lineNumber} is out of range (the file has {index.LineCount} lines).");
        }

        return (index.ReadLines(lineNumber, 1)[0].Text, null);
    }
}
