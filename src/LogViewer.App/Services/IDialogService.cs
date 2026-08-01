using LogViewer.App.ViewModels;
using LogViewer.Core.Configuration;
using LogViewer.Core.EventLogging;
using LogViewer.Core.ExternalTools;
using LogViewer.Core.Highlighting;
using LogViewer.Core.Search;

namespace LogViewer.App.Services;

public sealed record DirectoryWatchSelection(string DirectoryPath, string Pattern, bool AutoSwitchToLatestFile);

public sealed record EventLogSelection(string ChannelName, IReadOnlyList<EventLogFilterRule> Filters);

public interface IDialogService
{
    IReadOnlyList<string>? ShowOpenFileDialog();

    /// <summary>Opens the highlight rule editor over <paramref name="rules"/>. Returns true if the user saved changes.</summary>
    bool ShowHighlightRuleEditor(ICollection<HighlightRule> rules);

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

    void ShowServicesDialog();

    /// <summary>Opens a non-modal full-file/EventLog search window over <paramref name="document"/>.</summary>
    void ShowSearchDialog(TailDocumentViewModel document, IFullTextSearchService fileSearchService, IEventLogSearchService eventLogSearchService);

    /// <summary>Opens the per-document tab/MDI color-and-icon customization dialog. Returns true if the user saved changes.</summary>
    bool ShowCustomizeDialog(TailDocumentViewModel document);
}
