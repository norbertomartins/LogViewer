# LogViewer

A WPF (.NET 10) tail utility for text logs and Windows Event Log, in the spirit of tools like
BareTail/SnakeTail, but broader in scope. See [`PLAN.md`](PLAN.md) for the phase-by-phase history and
architecture decisions.

## Features

**Live tailing**
- Tailing of large text files with incremental reads (never rereads from scratch), encoding detection,
  and truncation/rotation/deletion handling ("circular logs") with no need to reopen.
- Directory + wildcard watching (`DirectoryWatch`), auto-switching to whichever matching file was most
  recently modified.
- Windows Event Log tailing (`Application`, `System`, custom channels) with independent, combinable regex
  filters (OR semantics across the enabled ones).
- Three window modes — Tabbed, Floating (AvalonDock), and classic MDI (child windows with
  drag/resize/cascade/tile) — sharing the same document view-model, so switching modes never loses scroll
  position, highlights, or bookmarks.

**Structured logs (Serilog/CLEF)**
- Automatic detection of Serilog/CLEF JSON log lines, parsing timestamp, level, rendered message,
  exception, and properties.
- Colorization by structured property (applied only to the message, not the whole line).
- Quick filter by `TraceId`/`SpanId` from a given line (click to filter on that value), a minimum log
  level filter, and a button to clear all active filters.

**Highlighting, bookmarks, and navigation**
- Highlight rules (regex, priority, color) evaluated live over incoming lines.
- Built-in highlight presets (e.g. "Errors & Exceptions", "Serilog Levels") plus an editor to
  create/export custom presets.
- Bookmarks and next/previous navigation (highlight or bookmark) via keyboard shortcuts
  (`F3`/`Shift+F3`, `F2`/`Shift+F2`, `Ctrl+F2`).

**Search**
- Full-text search over a file, independent of the in-memory ring buffer (finds matches already evicted
  from the tail), streaming and cancellable.
- Search across a whole Event Log channel, on a background thread.
- Non-modal search window per document — tailing keeps running while a search is in progress.

**Block analysis and similarity**
- Log block extraction and similarity comparison between blocks (block-diff), useful for comparing
  runs/occurrences of the same flow.
- Finding blocks similar to a selected block.

**External tools**
- External tool definitions with argument templating (`{FilePath}`, `{LineNumber}`, `{LineText}`),
  triggered manually ("Run Tool" menu, per-tool shortcut) or automatically when a line matches a highlight
  rule configured for auto-trigger (throttled to avoid process-launch storms).

**Customization and theming**
- Built-in Light/Dark themes, with a theme editor to adjust colors.
- Configurable font size for the log area.
- Per-document custom color and icon (title prefix on tabs/MDI, colored MDI title bar).

**Windows Services**
- Windows Services listing with start/stop.

**Session and persistence**
- Settings, recent sources, open documents (file, directory watch, or Event Log), window layout
  (Tabbed/Floating via AvalonDock, or MDI), main window position/maximized state, and the last active
  document are persisted to `%LOCALAPPDATA%\LogViewer\settings.json`, with schema migration.
- Drag-and-drop of a file or folder opens the document (or the "Open Directory (Watch)" dialog
  pre-filled).
- RDP session detection, widening the UI refresh interval to reduce traffic over Remote Desktop
  (configurable).
- System tray icon, with a per-tab file-change indicator.

**MCP server (optional)**
- An embedded Model Context Protocol server (Streamable HTTP, disabled by default — enable it and set the
  port in Settings) that lets an AI agent (Claude Desktop, Claude Code, or any MCP client) query the logs
  the app is tailing: list open documents, search text, fetch context around a line, list structured
  properties, find the most recurring message patterns, identify which functions/call-sites are logging
  the most errors, and reuse the block-diff/similarity engine.

## Solution layout

```
C:\Dev\LogViewer\
  LogViewer.slnx                 # solution (.slnx format)
  Directory.Build.props          # shared TFM/LangVersion/Nullable/analyzers
  global.json                    # pin/roll-forward to the installed .NET 10 SDK
  PLAN.md                        # detailed architecture and phase history
  README.md

  src\LogViewer.Core\            # net10.0, no WPF/UI reference — engine testable in isolation
    Tailing\                     # ITailSource, FileTailSource, DirectoryWatchTailSource,
                                  #   FileChangeDetector, RingLineBuffer, LineSplitter, EncodingDetector
    EventLogging\                # WindowsEventLogSource, EventLogFilterRule/Evaluator,
                                  #   EventLogSearchService, EventRecordFormatter
    Search\                      # IFullTextSearchService, FileFullTextSearchService (streaming/cancellable)
    Structured\                  # SerilogFormatDetector/EventParser, StructuredLogEvent,
                                  #   StructuredFileReader, StructuredFieldResolver, LogLevelSeverity
    BlockDiff\                   # LogBlockExtractor, BlockSimilarityScorer, FileBlockScanService,
                                  #   SimilarBlockFinder, BlockLookup, CorrelationKeySelector
    Highlighting\                # HighlightEngine, HighlightRule, HighlightPreset(+Seeds/ExportFile)
    Bookmarks\                   # BookmarkManager, Bookmark
    ExternalTools\                # ExternalToolDefinition, ExternalToolLauncher, ExternalToolContext
    Theming\                     # AppTheme, BuiltInThemes, ThemeColorKeys, ThemeBaseMode
    Configuration\                # AppSettings, ISettingsStore/JsonSettingsStore, TailSourceSettings,
                                  #   TailSourceKind, WindowLayoutSettings, McpServerSettings
    Analysis\                    # IPatternFrequencyAnalyzer/FilePatternFrequencyAnalyzer,
                                  #   ILineWindowReader/FileLineWindowReader, ExceptionFrameExtractor
    Documents\                    # IOpenDocumentCatalog, OpenDocumentInfo
    Services\Diagnostics\         # ProcessStatsService, RemoteSessionDetector
    Services\ServiceControl\      # ServiceControlService, WindowsServiceInfo

  src\LogViewer.Mcp\             # net10.0, no WPF; FrameworkReference Microsoft.AspNetCore.App
    McpServerHost.cs             # hosts the embedded Kestrel server (Streamable HTTP)
    ApiKeyMiddleware.cs          # optional API-key authentication
    ResponseLimits.cs            # per-call response size limits
    Tools\                       # LogDiscoveryTools, LogSearchTools, LogPatternTools, LogBlockTools

  src\LogViewer.App\             # net10.0-windows, UseWPF — presentation layer
    Views\Shell\                 # MainWindow, MdiHostView
    Views\Documents\             # TailDocumentView
    Views\Dialogs\                # Settings, Search, ExternalToolEditor, OpenDirectoryWatch,
                                  #   OpenEventLog, DocumentCustomize, HighlightPresetEditor,
                                  #   ThemeManager, Services
    ViewModels\                   # MainViewModel, TailDocumentViewModel, SettingsViewModel,
                                  #   SearchViewModel, ServicesViewModel, ThemeManagerViewModel, ...
    Controls\                     # DisplayLineCollection, MdiChildWindowControl, LogLineTemplateSelector
    Services\                     # DockingWindowModeHost, UiDispatcherLineSink, DialogService,
                                  #   ThemeService, WpfOpenDocumentCatalog, DarkTitleBar
    Converters\                   # binding converters (color, theme, log level, etc.)
    Models\                       # LogLineViewModel

  tests\LogViewer.Core.Tests\    # xUnit — Core engine (tailing, highlighting, analysis, block-diff, ...)
  tests\LogViewer.Mcp.Tests\     # xUnit — every MCP tool tested directly (bypassing HTTP)
  tests\LogViewer.App.Tests\     # xUnit — WPF-layer view-models and converters
  tests\LogViewer.UITests\       # end-to-end UI tests (FlaUI) driving the main window

  benchmarks\LogViewer.Benchmarks\  # BenchmarkDotNet — DisplayLineCollection, EventLogFilterEvaluator,
                                     #   full-text regex search, structured-line caching

  samples\block-diff\            # sample files for manually exercising block-diff

  .github\workflows\build.yml    # CI: build + tests on GitHub Actions
```

### Key packages

`CommunityToolkit.Mvvm`, `AvalonDock` (Tabbed/Floating docking), `Hardcodet.NotifyIcon.Wpf` (tray icon),
`Microsoft.Extensions.DependencyInjection` (composition root), `System.Diagnostics.EventLog`,
`System.ServiceProcess.ServiceController`, `System.Text.Encoding.CodePages`,
`ModelContextProtocol`/`ModelContextProtocol.AspNetCore` (MCP server), `xunit`, `FlaUI` (UI tests),
`BenchmarkDotNet` (benchmarks).

## Running

```bash
dotnet build LogViewer.slnx -c Debug
dotnet test tests\LogViewer.Core.Tests\LogViewer.Core.Tests.csproj
dotnet test tests\LogViewer.Mcp.Tests\LogViewer.Mcp.Tests.csproj
dotnet run --project src\LogViewer.App\LogViewer.App.csproj
```

The MCP server is disabled by default — enable it and set the port in Settings (restart required to
apply).

See [`PLAN.md`](PLAN.md) for architecture details, design decisions, and the per-phase verification
history.
