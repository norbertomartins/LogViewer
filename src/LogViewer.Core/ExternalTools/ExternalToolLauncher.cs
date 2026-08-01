using System.Diagnostics;
using System.IO;

namespace LogViewer.Core.ExternalTools;

/// <summary>
/// Substitutes <see cref="ExternalToolContext"/> values into an <see cref="ExternalToolDefinition.ArgumentTemplate"/>
/// and launches the tool. Never throws — launch failures are reported back via <paramref name="error"/> so callers
/// can surface them the same way the rest of the app reports non-fatal errors (a <c>StatusMessage</c>), rather than
/// crashing the tailing session over a bad tool path.
/// </summary>
public static class ExternalToolLauncher
{
    /// <summary>Replaces <c>{FilePath}</c>, <c>{LineNumber}</c>, and <c>{LineText}</c> placeholders. Missing
    /// context (e.g. no selected line) substitutes an empty string rather than leaving the placeholder in place.</summary>
    public static string BuildArguments(string argumentTemplate, ExternalToolContext context)
    {
        return argumentTemplate
            .Replace("{FilePath}", context.FilePath)
            .Replace("{LineNumber}", context.LineNumber?.ToString() ?? string.Empty)
            .Replace("{LineText}", context.LineText ?? string.Empty);
    }

    public static bool TryLaunch(ExternalToolDefinition tool, ExternalToolContext context, out string? error)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = tool.ExecutablePath,
                Arguments = BuildArguments(tool.ArgumentTemplate, context),
                UseShellExecute = false,
            };

            Process.Start(startInfo);
            error = null;
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            error = $"Failed to launch '{tool.Name}': {ex.Message}";
            return false;
        }
    }
}
