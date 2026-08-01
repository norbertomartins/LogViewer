namespace LogViewer.Core.ExternalTools;

/// <summary>The tailing context available for <see cref="ExternalToolLauncher"/> argument substitution.</summary>
public sealed record ExternalToolContext(string FilePath, long? LineNumber, string? LineText);
