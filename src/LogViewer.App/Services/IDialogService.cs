using LogViewer.App.Models;
using LogViewer.App.ViewModels;
using LogViewer.Core.BlockDiff;
using LogViewer.Core.Configuration;
using LogViewer.Core.EventLogging;
using LogViewer.Core.ExternalTools;
using LogViewer.Core.Highlighting;
using LogViewer.Core.Search;

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
}
