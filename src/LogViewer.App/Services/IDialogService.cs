using LogViewer.App.Models;
using LogViewer.App.ViewModels;
using LogViewer.Core.Analysis;
using LogViewer.Core.BlockDiff;
using LogViewer.Core.Configuration;
using LogViewer.Core.EventLogging;
using LogViewer.Core.ExternalTools;
using LogViewer.Core.Highlighting;
using LogViewer.Core.Search;
using LogViewer.Core.Structured;
using LogViewer.Core.Tailing;

namespace LogViewer.App.Services;

public sealed record DirectoryWatchSelection(string DirectoryPath, string Pattern, bool AutoSwitchToLatestFile);

public sealed record EventLogSelection(string ChannelName, IReadOnlyList<EventLogFilterRule> Filters);

public sealed record HttpTailSelection(string Url, string Mode, IReadOnlyList<string> Headers);

public sealed record ProcessTailSelection(string FileName, string Arguments, bool RestartOnExit);

public sealed record SshTailSelection(
    string Host, int Port, string Username, string? Password,
    string? PrivateKeyPath, string? PrivateKeyPassphrase, string Command,
    string? HostKeyFingerprintSha256, bool AcceptAnyHostKey);

public sealed record EtwTailSelection(string Provider, int Level);

public interface IDialogService
{
    IReadOnlyList<string>? ShowOpenFileDialog();

    /// <summary>Picks a single .wav file for the global sound-alert setting. Returns the path, or null if cancelled.</summary>
    string? ShowOpenSoundFileDialog();

    /// <summary>Asks where to save an incident report (Markdown or HTML, picked by the chosen extension). Returns the
    /// path, or null if cancelled.</summary>
    string? ShowSaveIncidentReportDialog(string suggestedFileName);

    /// <summary>Opens the "merge files/folders by time" builder. Returns the resolved concrete file paths
    /// (folder entries already expanded), or null if cancelled.</summary>
    IReadOnlyList<string>? ShowOpenMergedSourcesDialog();

    /// <summary>Opens the highlight preset editor over <paramref name="presets"/>. Returns true if the user saved changes.</summary>
    bool ShowHighlightPresetEditor(ICollection<HighlightPreset> presets);

    /// <summary>Opens the external tool editor over <paramref name="tools"/>. <paramref name="availableHighlightRules"/>
    /// populates the auto-trigger rule picker. Returns true if the user saved changes.</summary>
    bool ShowExternalToolEditor(ICollection<ExternalToolDefinition> tools, IReadOnlyList<HighlightRule> availableHighlightRules);

    /// <summary>Opens the settings dialog over <paramref name="settings"/>. Returns true if the user saved changes.</summary>
    bool ShowSettings(AppSettings settings);

    /// <summary>Opens the theme manager (new/duplicate/edit/delete + pick active) over <paramref name="settings"/>.
    /// Returns true if the user saved changes.</summary>
    bool ShowThemeManager(AppSettings settings);

    DirectoryWatchSelection? ShowOpenDirectoryWatchDialog(string? initialDirectoryPath = null);

    EventLogSelection? ShowOpenEventLogDialog();

    HttpTailSelection? ShowOpenHttpTailDialog();

    ProcessTailSelection? ShowOpenProcessTailDialog();

    /// <summary>Opens the Docker/Kubernetes container-logs picker. Returns what to follow, or null if cancelled.</summary>
    ContainerLogRequest? ShowOpenContainerLogsDialog();

    SshTailSelection? ShowOpenSshTailDialog();

    EtwTailSelection? ShowOpenEtwTailDialog();

    /// <summary>Shows the Ctrl+P command palette over <paramref name="commands"/>. Returns the chosen
    /// command (whose <c>Execute</c> the caller then runs), or null if dismissed.</summary>
    PaletteCommand? ShowCommandPalette(IReadOnlyList<PaletteCommand> commands);

    /// <summary>Single-line text prompt (e.g. "name this session profile"). Returns the entered text,
    /// or null if cancelled.</summary>
    string? ShowTextPrompt(string title, string prompt, string? initialValue = null);

    void ShowServicesDialog();

    /// <summary>Opens a non-modal full-file/EventLog search window over <paramref name="document"/>.</summary>
    void ShowSearchDialog(TailDocumentViewModel document, IFullTextSearchService fileSearchService, IEventLogSearchService eventLogSearchService);

    /// <summary>Opens the per-document tab/MDI color-and-icon customization dialog. Returns true if the user saved changes.</summary>
    bool ShowCustomizeDialog(TailDocumentViewModel document);

    /// <summary>Opens the non-modal "Find Similar Block" comparison window, anchored at <paramref name="anchorLine"/>
    /// in <paramref name="sourceDocument"/>. <paramref name="openDocuments"/> populates the comparison-target picker
    /// alongside a "browse for file" option.</summary>
    void ShowSimilarBlockDialog(
        TailDocumentViewModel sourceDocument,
        LogLineViewModel anchorLine,
        IReadOnlyList<TailDocumentViewModel> openDocuments,
        ISimilarBlockFinder blockFinder);

    /// <summary>Opens the non-modal per-document statistics panel (counts, lines/sec, top recurring
    /// message patterns) over <paramref name="document"/>.</summary>
    void ShowDocumentStatsDialog(TailDocumentViewModel document, IPatternFrequencyAnalyzer patternAnalyzer);

    /// <summary>Opens the non-modal "Compare Files" whole-file side-by-side diff window.
    /// <paramref name="openPath"/> lets the user jump to a diff line by opening (or focusing) that file
    /// as a live-tailed document.</summary>
    void ShowCompareFilesDialog(Action<string, long> openPath);

    /// <summary>Opens the non-modal SerilogTracing "Trace Tree" panel over <paramref name="document"/>'s
    /// currently buffered lines. <paramref name="initialTraceId"/> preselects a trace (e.g. from a
    /// right-clicked line), or null to default to the most recent one.</summary>
    void ShowTraceTreeDialog(TailDocumentViewModel document, string? initialTraceId);

    /// <summary>Opens the non-modal virtualized whole-file browser backed by <paramref name="viewModel"/>.</summary>
    void ShowFileBrowserDialog(FileBrowserViewModel viewModel);

    /// <summary>Opens the non-modal grouped-exceptions panel over <paramref name="document"/>.</summary>
    void ShowExceptionGroupsDialog(TailDocumentViewModel document);

    /// <summary>Opens the modal custom-log-format editor over <paramref name="formats"/>, with the preview prefilled
    /// from <paramref name="sampleLines"/>. Returns the edited list, or null if cancelled.</summary>
    IReadOnlyList<CustomLogFormat>? ShowCustomFormatsEditor(IReadOnlyList<CustomLogFormat> formats, IReadOnlyList<string> sampleLines);
}
