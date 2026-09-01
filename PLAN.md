# LogViewer — Tail Utility for Text Logs & Windows EventLog

## Status

- **Phase 1 — done.** Solution scaffolding, Core tailing engine, tabbed/floating window modes, highlighting, bookmarks, settings persistence. Verified via 36 unit tests + manual UI pass.
- **Phase 2 — done.** Windows EventLog tailing + filters, MDI window mode, directory/wildcard auto-switch tailing, service start/stop, title-bar process stats, tray icon, tab file-change indicator. Verified via 42 unit tests (6 new) + a full manual UI pass exercising every feature in the running app, including catching and fixing two real bugs found only through that manual testing (see below).
- **Phase 3 — done.** External tool definitions are now fully wired: manual invocation (toolbar "Run Tool" menu + per-tool shortcut gestures) and auto-trigger on highlight match, via `ExternalToolLauncher` (Core). Full-file search (`FileFullTextSearchService`) and full-EventLog-channel search (`EventLogSearchService`), both streaming/cancellable, surfaced through a non-modal Search window per document. Per-document tab-text/MDI-title-bar color and icon-glyph customization, persisted across restarts. Verified via 55 unit tests (13 new) + build + a non-interactive startup smoke test; see caveat below.
- **Phase 4 — done.** `TailSourceSettings` generalized to cover all three source kinds (File/DirectoryWatch/EventLog) plus per-document customization and MDI bounds, so session-restore now reopens directory watches and EventLog sources (previously silently dropped) and restores the last-active document. Main window bounds/maximize state and the AvalonDock docking layout (tab/float arrangement) now persist and restore across restarts. Directory drag-drop opens the "Open Directory (Watch)" dialog pre-filled with the dropped folder. RDP sessions auto-widen the UI redraw-batching interval (toggle in Settings) via `RemoteSessionDetector`. Verified via unit tests + build + startup smoke test; see caveat below.

- **Phase 5 — done.** An embedded MCP (Model Context Protocol) server, disabled by default, lets an
  external AI agent (Claude Desktop, Claude Code, or any MCP client) query the logs this app is tailing —
  discover open documents, search, pull context around a line, find the most frequent recurring message
  patterns, and rank which functions/call-sites are repeatedly logging errors, plus reuse the existing
  block-diff/similarity engine. Verified via 21 new `LogViewer.Mcp.Tests` unit tests (150 total across both
  test projects) + build + a live JSON-RPC handshake against a running instance; see caveat below.

- **Phase 6a — done.** Structured parsing generalized beyond Serilog/CLEF: a new `ILogLineParser`
  abstraction (`src/LogViewer.Core/Structured/`) with implementations for **logfmt** (`key=value`),
  **generic NDJSON / JSON-lines** (MEL JSON console, pino/Bunyan/Winston/zap — field-name-alias driven),
  **syslog** (RFC 5424 + legacy RFC 3164/BSD, PRI→severity→level, structured-data elements → properties),
  and **W3C Extended / IIS** (stateful `#Fields:`-driven column split, `sc-status` → level). Serilog is
  wrapped as `SerilogLogLineParser`. `LogLineParsers` is the registry + priority-ordered sample-based
  auto-detection (`Detect` / `DetectFile`), replacing the Serilog-only `SerilogFormatDetector.SniffFile`
  call sites in `MainViewModel` (file open + directory watch + session restore). `TailDocumentViewModel`
  now holds a per-document `ILogLineParser` (chosen at construction from the detected `StructuredFormatId`)
  instead of calling the static Serilog parser, and surfaces `StructuredFormatName` next to the
  "Structured View" toggle. Verified via 31 new Core unit tests (222 total across the three test projects)
  + full solution build.

- **Phase 6b — done.** `TailSourceSettings.StructuredFormatId` (schema-compatible nullable add, no
  migration) persists a manually-pinned format; `TailDocumentViewModel.StructuredFormatId` is now a
  settable property (rebuilds the parser + reprocesses) with `AvailableStructuredFormats` for a toolbar
  picker combo in `TailDocumentView`, and `IsStructuredFormatManuallyChosen` gates persistence the same
  way `IsStructuredView`'s null-means-auto does. `MainViewModel` gained `FindExistingFormatOverride` and
  threads the override through file open / directory watch / session restore. `StructuredFileReader` got
  an `ILogLineParser` overload and its legacy `ReadAsync(path, ct)` now auto-detects the format (falling
  back to Serilog), so every MCP tool built on it (`FilePatternFrequencyAnalyzer`, `FileBlockScanService`)
  handles all five formats with no further change. `.gz` archives: `CompressedLogFile` (Core) sniffs the
  gzip magic bytes and decompresses once into a stamped temp file that the normal file pipeline opens;
  `MainViewModel.OpenPath` materializes on open, keeping the original path for the recent list and the
  tab title. Verified via 12 new Core unit tests (227 total across the three test projects) + full build.
  Not yet done at that point: the "competing with mature tools" items.

- **Phase 6c (partial) — done.** **Live display filter over raw line text:** `TailDocumentViewModel`
  gained `TextFilterPattern` / `TextFilterExclude` / `TextFilterIsRegex` / `TextFilterCaseSensitive`,
  a compiled-regex cache with a 250 ms match timeout, invalid-pattern handling (never hides everything;
  surfaces `StatusMessage`), `IsTextFilterActive`, an extended `FilterStatusText`, and a
  `PassesTextFilter(string)` predicate the view's `ICollectionView.Filter` now ANDs in alongside the
  existing trace/span/level filters — works in both plain and structured view. Toolbar gained a filter
  box + `.*` / `Aa` / `Exclude` / clear buttons. **Export:** `ExportVisibleCommand` raises
  `ExportRequested`; the view writes `LineListView.Items` (post-filter) to a `SaveFileDialog` target,
  reporting count/errors via `StatusMessage`. Verified via 3 new App unit tests (230 total across the
  three test projects) + full build.

- **Phase 6c cont. — sub-string highlight spans done.** `HighlightSpan(int Start, int Length)` (Core);
  `HighlightMatch` carries `IReadOnlyList<HighlightSpan> Spans` (a 3-arg ctor overload keeps existing
  call sites source-compatible). `HighlightEngine.Evaluate` now also returns every matched range —
  `regex.Matches` for regex rules, an all-occurrences scan for keyword rules — but only for rules that
  target the raw line (property-target rules stay whole-line, `Spans` empty). App:
  `LogLineViewModel.HighlightSpans`, a `HighlightSpanInlinesConverter` rendering the matched
  sub-string(s) **bold + underline** on the plain-line template via the existing `InlinesHelper`, gated
  by `AppSettings.HighlightMatchSpans` (default true, schema-compatible) propagated as
  `TailDocumentViewModel.ShowHighlightMatchSpans` with a Settings checkbox. Structured rows keep
  whole-line coloring (span indices are against the raw line, not the rendered message). Verified via
  3 new Core unit tests (233 total across the three test projects) + full build.

- **Phase 6c cont. — volume timeline done.** `LogVolumeBinner` (Core/Analysis): pure bucketing of
  `VolumeSample(Timestamp, Severity, LineNumber)` into consecutive fixed-width `VolumeBin`s
  (Total/Warnings/Errors + first/last line number), auto-choosing a "nice" bucket width for a target
  bin count, filling empty gap buckets, and capping the bin count. App: `TailDocumentViewModel` builds
  samples from the displayed lines' `Structured.Timestamp`/`Level`, exposes `VolumeBins` /
  `ShowTimeline` / `MaxBinTotal` / `TimelineHasData`, recomputes on a 400 ms throttle after
  `Lines` changes (and immediately on toggle-on), and `SelectBinCommand` scrolls to a bucket's first
  line. `TailDocumentView` gained a collapsible timeline strip (row 1) — stacked error/warn/info bars
  per bucket, click to jump — behind a "📊 Timeline" toolbar toggle, plus `VolumeBinBarHeightConverter`.
  Only timestamped lines contribute (all five structured formats qualify). Verified via 5 new Core unit
  tests (238 total) + full build.

- **Phase 6c cont. — multi-file merge-by-timestamp done.** `MergedTailSource : ITailSource` (Core)
  composes N `FileTailSource`, extracts a leading timestamp per line via `MergedTimestampExtractor`
  (ISO-8601 / `yyyy-MM-dd HH:mm:ss,fff` / time-only), carries forward the last timestamp for
  continuation lines, and passes everything through a bounded **reorder buffer** (default 2s window,
  timer-driven flush, deterministic `FlushDueAt(now)` seam) that sorts the due lines by timestamp before
  emitting them with sequential line numbers and a `label│ ` prefix (base filename, `#n`-disambiguated).
  `TailSourceKind.MergedFiles` + `TailSourceSettings.MergedPaths` (schema-compatible) persist and restore
  the set; `MainViewModel.OpenMergedFiles` (File ▸ "Open Merged Files (by time)…", multi-select) with an
  order-independent dedup key; `TailDocumentViewModel.Kind` maps the source; merged docs are excluded
  from Recent Files and aren't auto-structured (the label prefix would break JSON parsing). Verified via
  17 new Core + 2 new App unit tests (254 total across the three test projects) + full build.

- **Phase 6d — remote HTTP + WebSocket tailing done.** `HttpTailSource : ITailSource` (Core) tails a log
  endpoint over HTTP(S): `HttpTailMode.Stream` holds one `ResponseHeadersRead` response open and reads it
  line by line (SSE `data:` frames unwrapped, control frames skipped) or chunked plain text;
  `HttpTailMode.Poll` re-requests on an interval and emits the lines new since last time, raising
  `SourceReset` when the body shrinks (rotation); `Auto` picks between them from the first response.
  `WebSocketTailSource : ITailSource` connects a `ClientWebSocket` and treats each complete text message
  as one or more whole lines (`WebSocketFrames.SplitMessage`). Both reconnect with a linear backoff and
  take verbatim handshake/request headers. App: `TailSourceKind.RemoteHttp` / `RemoteWebSocket` +
  `TailSourceSettings.HttpMode`/`HttpHeaders` (schema-compatible), an `OpenHttpTailView` dialog
  ("Open Remote Log Endpoint" — URL / mode / `Name: Value` headers), `MainViewModel.OpenRemoteEndpoint`
  (routes by URL scheme) + File ▸ "Open Remote Log Endpoint…", session restore, `Kind` mapping.
  Verified via 12 new Core + 3 new App unit tests (264 total) + full build.

- **Phase 6e — process / SSH / ETW sources done.** Three more `ITailSource` implementations (Core):
  `ProcessTailSource` spawns a command and tails its stdout/stderr line by line, relaunching a
  follow-style command on exit with linear backoff (covers `journalctl -f`, `docker logs -f`,
  `kubectl logs -f`, `adb logcat`, …). `SshTailSource` (SSH.NET dependency) runs a command over SSH and
  streams its output, with key-file or password auth, host-key fingerprint verification (or explicit
  opt-out), and reconnect — secrets are held only in memory, never persisted. `EtwTailSource`
  (`Microsoft.Diagnostics.Tracing.TraceEvent`) consumes a real-time ETW provider by name or GUID,
  raising a clear "run as Administrator" error when not elevated. App: `TailSourceKind.Process`/`Ssh`/`Etw`
  + `TailSourceSettings` fields (SSH secrets excluded), three dialogs (`OpenProcessTailView` /
  `OpenSshTailView` / `OpenEtwTailView`) with `PasswordBox`-backed secret entry, `MainViewModel.Open*`
  methods + File-menu items, session restore (SSH only for key-based auth), `Kind` mapping. Verified via
  7 new Core + 2 new App unit tests (273 total) + full build. journald has no dedicated source — it is
  covered by `ProcessTailSource` running `journalctl -f` (locally or `wsl journalctl`).

- **Phase 6f — first interactive-UI pass + fixes.** Ran the app under FlaUI and fixed what the pass
  turned up:
  - **Merged view + structured toggle did nothing** — merged lines carry a `label│ ` prefix that broke
    every parser. `MergedTailSource.StripLabel` now removes it; `TailDocumentViewModel` strips it before
    parsing when the source is a `MergedTailSource`, and `MainViewModel.OpenMergedFiles` auto-detects the
    format from the underlying files and turns structured view on.
  - **Volume timeline only worked for structured docs** — `RecomputeTimeline` now falls back to
    `MergedTimestampExtractor` + `LogLevelNormalizer.GuessSeverityFromLine` for plain-text lines, so a
    plain `yyyy-MM-dd HH:mm:ss [LEVEL]` log also charts.
  - **ETW delivered no events** — switched from `Source.Dynamic.All` (EventSource/manifest only) to
    `Source.AllEvents`, skipping only session-bookkeeping events.
  - **Compact toolbar** — the document toolbar's text buttons are now single-glyph icons, each keeping
    its `ToolTip` and an `AutomationProperties.Name`.
  - **Sample logs** — `samples/timeline/{orders-service.clef, payments-service.log}` (+ a deterministic
    `generate.py`) covering the same 15-minute window with a volume burst and an error storm, for
    exercising the timeline and merged view.
  - **UI automation** — `LogViewer.UITests` now drives the app through UIA patterns (Invoke / Toggle /
    ExpandCollapse / Value) instead of synthesized mouse input, so the suite runs in a locked session.
    New `DocumentUITests` (restores a sample log via an isolated `settings.json`, then asserts lines
    render, the icon toolbar is reachable by accessible name, the timeline toggles + draws bars, and a
    text filter hides lines) plus a File-menu-completeness check.
  Verified via new Core/App unit tests + the FlaUI suite. Still no manual pass of the SSH / ETW dialogs
  against real remote hosts / an elevated session.

- **Phase 6g — second UI-feedback pass.**
  - **ETW dialog** — added a **Debug** level (maps to ETW byte `0xFF` = verbose + any provider-defined
    level above it); the Level combo was being clipped by the fixed window height, now `SizeToContent`
    with a `MinWidth` on the combo.
  - **Merge from many folders / whole directories** — replaced the single multi-select `OpenFileDialog`
    behind File ▸ "Open Merged Files / Folders (by time)…" with a builder dialog
    (`OpenMergedSourcesView`/`ViewModel`, `IDialogService.ShowOpenMergedSourcesDialog`). It accumulates
    loose files added across repeated pickers (so from different folders) and/or folder entries
    (directory + wildcard) that expand to their matching files on OK, de-duplicated and order-preserving.
    This also covers "open one or several directories and merge them". `MainViewModel.OpenMergedFiles`
    still receives a flat resolved file list, so persistence/restore/dedup are unchanged.

- **Phase 6h — UX / platform pass.**
  - **Command palette (Ctrl+P)** — `CommandPaletteView`/`ViewModel` + `IDialogService.ShowCommandPalette`.
    Fuzzy-ranked (title-prefix › substring › subsequence) over the menu actions, one "Go to…" per open
    document, the highlight-preset toggles, and the active document's commands. `MainViewModel.
    BuildPaletteCommands()` assembles the list; the chosen `PaletteCommand.Execute` runs after close.
  - **Embedded pattern tester** — `PatternMatchHelper` (shared regex/substring match-range logic) +
    `RegexTestInlinesConverter`. A "Pattern tester" section in the highlight-rule editor and a 🧪 popup
    on the document filter box: paste sample lines, matches highlight live as the pattern/regex/case
    settings change, with an "N / M lines match" summary.
  - **Named session profiles** — `SessionProfile` (Core): a named snapshot of the open documents (each a
    `TailSourceSettings`), window mode, docking layout, active document. `AppSettings.SessionProfiles`,
    schema **v5→v6** (no-op migration; `TailSourceSettings` also gains persisted per-document text/level
    filter fields, so ordinary restore now remembers filters too). `MainViewModel` Save/Load/Delete +
    `SaveSessionProfileAs` (name via `IDialogService.ShowTextPrompt`); `RestoreSession` refactored into a
    shared `RestoreSources()` used by both startup and profile switching. New "Session" menu + palette
    entries; `MainWindow` captures the live AvalonDock XML into the profile on save.
  - **Smart auto-scroll lock** — `TailDocumentView.OnLineListScrollChanged` pauses follow when the user
    scrolls up off the tail and re-arms it at the bottom; programmatic `ScrollIntoView` is flagged
    (`IsProgrammaticScroll`) so it isn't mistaken for a user gesture. `UnseenLineCount` drives an
    "⤓ N new lines — resume follow" banner.
  - **Performance status bar** — `MainViewModel.PerformanceStatus` (polled ~1 Hz): lines/s, ring-buffer
    fill + approx MB, worst-case UI dispatch latency, process RAM. Backed by
    `RingLineBuffer.RetainedTextLength` and `UiDispatcherLineSink.AverageFlushMilliseconds` (stopwatch
    around each flush, EMA-smoothed).

- **Phase 6i — localization (restart-based).** `LogViewer.App/Localization/`: `Loc` (a `ResourceManager`
  wrapper) + `LocExtension` (`{loc:Loc Key}` XAML markup extension, resolves once at parse time) over
  `Strings.resx` (neutral, English — values byte-identical to the former hard-coded text) and
  `Strings.pt-PT.resx`. `AppSettings.Language` (culture name, default `en`) is applied once in
  `App.OnStartup` via `Loc.Initialize` *before the first window is built*; a language change needs a
  restart, so `Loc` is a plain static lookup with no change notification. When no language is selected
  `Loc` pins lookups to `InvariantCulture` so the neutral text is returned regardless of the OS UI
  language. Every XAML view and every user-facing string built in a ViewModel / code-behind
  (`StatusMessage`s, window title, performance readout, command-palette entries, composed filter status)
  now goes through the bundle. Schema **v6→v7** (no-op migration — the field initializer already gives
  pre-v7 files `"en"`). Settings dialog gains a Language dropdown (English / Português (Portugal)).
  `LocTests` guards neutral↔pt-PT round-trip and that every neutral key has a pt-PT translation.
  Adding a language: drop in `Strings.<culture>.resx` and add a `LanguageOption` to
  `SettingsViewModel.AvailableLanguages`.

### Phase 5 verification caveat
Every tool class is unit tested directly (bypassing the HTTP transport) against real fixture files, and
the whole solution builds. Beyond that, a real end-to-end pass was run non-interactively: the app was
launched with `Mcp.Enabled=true`, Kestrel logged `Now listening on: http://127.0.0.1:38173`, and raw
`initialize` / `tools/list` / `tools/call` (`logs_list_open_documents`) JSON-RPC requests over Streamable
HTTP (via `curl`) all round-tripped correctly — `tools/list` returned all 11 tools with correct schemas,
and the `logs_list_open_documents` call correctly resolved the live `MainViewModel` through the shared DI
singletons. The process was then force-killed (no interactive window to close gracefully in that
environment) and the port was confirmed free afterward. **Still needs a manual pass in an interactive
session**: closing the app window normally to confirm `OnExit`/`McpServerHost.StopAsync` releases the port
gracefully (rather than relying on OS cleanup after a forced kill), driving the handshake from a real MCP
client (Claude Desktop/Code or the MCP Inspector) instead of raw `curl`, and the Settings dialog's new
checkbox/port field.

### Phase 3/4 verification caveat
Unlike Phases 1–2, this pass did **not** include a live screenshot-driven UI walkthrough — the environment that implemented Phase 3/4 only had build/test/non-interactive-launch tooling available, not native WPF UI automation. Everything above is confirmed via `dotnet build`, the unit test suite (55/55 green), and a background `dotnet run` smoke test (process starts cleanly, no crash/error output, existing settings file left untouched). The interactive paths — external tool launch/auto-trigger, search-dialog results and jump-to-line, tab/MDI customization dialog, directory drag-drop, and window-bounds/AvalonDock-layout restore across a real restart — still need a manual pass in a running session before being considered fully verified end-to-end.

### Phase 2 bugs found and fixed during manual verification
1. `MainViewModel` constructor threw `InvalidOperationException: Collection was modified` on startup when session-restore was enabled, because `OpenPath` mutates `RecentSources` while a `.Where(...)` over that same list was still being enumerated. Fixed with `.ToList()` before the loop.
2. `DirectoryWatchTailSource` raised `SourceReset` *after* starting the new file's `FileTailSource`, so the newly delivered lines arrived before the reset that clears the display — the switch silently wiped out the new file's content. Fixed by raising `SourceReset` before attaching/starting the new file source; added a regression test asserting event order.

## Context

`C:\Dev\LogViewer` started as an empty directory — this is a greenfield WPF desktop app (.NET 10) modeled on tools like BareTail/SnakeTail, but broader: live tailing of large text log files *and* Windows Event Logs, with MDI/Tabbed/Floating window modes, regex-capable highlighting, bookmarks, external-tool integration, circular-log handling, directory/wildcard tailing, full-file search, EventLog filtering, tray support, and live process stats in the title bar.

The full feature list is large (15+ major features), so the user approved a **phased approach**: design the whole architecture up front, but implement in phases so each one stays reviewable and testable rather than landing one enormous, hard-to-review change.

Confirmed decisions (approved, not open for re-litigation):
- NuGet packages are fine to use (CommunityToolkit.Mvvm, AvalonDock, Hardcodet.NotifyIcon.Wpf).
- MDI mode (classic overlapping child windows) has no native WPF control and no actively-maintained package — hand-built as a Canvas-based container.
- Solution splits into **LogViewer.Core** (UI-free, testable engine) + **LogViewer.App** (WPF/MVVM) + **LogViewer.Core.Tests** (xUnit).

## Solution Layout

```
C:\Dev\LogViewer\
  LogViewer.slnx
  Directory.Build.props        # shared TFM/LangVersion/Nullable/analyzers
  .editorconfig
  global.json                  # pin/roll-forward to installed .NET 10 SDK

  src\LogViewer.Core\          # net10.0, no WPF/UI references
    Tailing\                   # FileTailSource, DirectoryWatchTailSource, RingLineBuffer, etc.
    Highlighting\
    Bookmarks\
    Configuration\
    ExternalTools\             # ExternalToolDefinition, ExternalToolLauncher (arg-template substitution + Process.Start)
    EventLogging\               # WindowsEventLogSource, EventLogFilterRule, EventLogSearchService, EventRecordFormatter/FilterEvaluator
    Search\                     # IFullTextSearchService, FileFullTextSearchService (streaming, cancellable)
    Analysis\                   # IPatternFrequencyAnalyzer, ILineWindowReader, ExceptionFrameExtractor (Phase 5)
    Documents\                  # IOpenDocumentCatalog, OpenDocumentInfo (Phase 5)
    Services\Diagnostics\       # ProcessStatsService, RemoteSessionDetector
    Services\ServiceControl\    # ServiceControlService, WindowsServiceInfo

  src\LogViewer.Mcp\           # net10.0, no WPF; FrameworkReference Microsoft.AspNetCore.App (Phase 5)
    Tools\                      # LogDiscoveryTools, LogSearchTools, LogPatternTools, LogBlockTools

  src\LogViewer.App\           # net10.0-windows, UseWPF
    Views\Shell\   Views\Documents\   Views\Dialogs\
    ViewModels\
    Controls\                  # DisplayLineCollection, MdiChildWindowControl
    Services\
    Converters\
    Models\

  tests\LogViewer.Core.Tests\  # net10.0, xUnit — 129 tests
  tests\LogViewer.Mcp.Tests\   # net10.0, xUnit — 21 tests (Phase 5)
```

Key packages: `CommunityToolkit.Mvvm`, `AvalonDock` (Tabbed + Floating docking panes), `Hardcodet.NotifyIcon.Wpf` (tray icon), `Microsoft.Extensions.DependencyInjection` (composition root), `System.Diagnostics.EventLog`, `System.ServiceProcess.ServiceController`, `System.Text.Encoding.CodePages`, `ModelContextProtocol`/`ModelContextProtocol.AspNetCore` (Phase 5), `xunit`.

## Core Tailing Engine

`ITailSource` (Core) exposes batched events — `LinesRead` (a whole read-cycle's worth of lines, never per-line), `SourceReset` (truncation/rename/rotation), `Error` — so downstream consumers never process one line at a time under load. `FileTailSource`, `DirectoryWatchTailSource`, and `WindowsEventLogSource` all implement it, so `TailDocumentViewModel` hosts any of them identically.

`FileTailSource`:
- Opens with `FileShare.ReadWrite | FileShare.Delete`; never blocks external writers/rotators.
- `FileSystemWatcher` on the parent directory as the primary wake signal, **plus** a fallback poll timer (default 250ms) since `FileSystemWatcher` is known to miss/coalesce events under heavy writers or UNC paths.
- Reads only `[lastOffset, currentLength)` via pooled buffers (`ArrayPool<byte>`) — never rereads from 0, never loads the whole file.
- `LineSplitter` is stateful, carrying a small pending-partial-line buffer across read boundaries.
- Initial open reads only the **tail** (reverse chunk-scan from EOF, default ~1000 lines) so opening a multi-GB file is instant and memory-bounded.

`FileChangeDetector` — truncation/rotation handling (the "circular logs" feature):
- Tracks per-file identity via `GetFileInformationByHandle` (NTFS file ID), not just the path.
- Missing file → `Reset(Deleted)`; different file ID → `Reset(Rotated)`; same ID but shorter → `Reset(Truncated)`.
- `TailDocumentViewModel` reacts to `SourceReset` by clearing its ring buffer and inserting a "── file truncated/rotated ──" marker, then resumes — no restart needed.

`DirectoryWatchTailSource` — watches a directory + wildcard pattern, auto-switches to the most recently modified match by composing an inner `FileTailSource` and forwarding its events. **Reset must be raised before the new file's content starts flowing** (see bug #2 above) — consumers clear their display on reset, so firing it after `Start()` wipes out content that was just delivered.

`RingLineBuffer` — bounded circular buffer (default capacity 50,000 lines, configurable), O(1) amortized append, evicts oldest on overflow. Deliberately **not** an `ObservableCollection` — no per-item `CollectionChanged`, since that alone would blow the >100 lines/sec budget.

**UI-side throughput bridge** (`UiDispatcherLineSink` in App): background-thread events are queued (lines AND resets, in arrival order); a single throttled dispatch (~every 50–100ms) drains the queue and raises consolidated updates. The list view (`ListView` + `VirtualizingStackPanel`, `VirtualizationMode="Recycling"`) binds to `DisplayLineCollection`, a bounded collection raising one `Reset` notification per batch rather than per line.

## EventLog Engine

`WindowsEventLogSource` uses `System.Diagnostics.Eventing.Reader.EventLogWatcher` against a named channel (`"Application"`, `"System"`, etc.) — these channels grant read to `BUILTIN\Users` by default, so live subscription works **without admin rights** (confirmed via manual testing — it picked up real Application-log entries live). The `Security` channel and custom app channels with restrictive ACLs surface a clear "requires elevated permissions" error via `Error` rather than failing silently.

`EventLogFilterRule { Guid Id, string Name, string? ProviderName, string RegexPattern, bool IsEnabled, EventLogFilterField Field }` — per-source regex filters, independently toggleable. Semantics: no enabled filter → everything passes; at least one enabled → an event must match at least one enabled filter (OR).

## MVVM Structure & Window-Mode Hosting

Core models are plain records/POCOs with no WPF dependency: `HighlightRule`, `Bookmark`, `ExternalToolDefinition`, `EventLogFilterRule`, `AppSettings`.

**`TailDocumentViewModel`** is the single document view-model shared across all three window modes — wraps one `ITailSource`, its `RingLineBuffer`, `HighlightEngine`, `BookmarkManager`, plus MDI-mode-only bounds (`MdiLeft/Top/Width/Height/IsMdiMaximized/MdiZIndex`, ignored by Tabbed/Floating).

**Window-mode hosting** — `DockingWindowModeHost` backs Tabbed + Floating with one AvalonDock `DockingManager` (floating is just AvalonDock's native float/dock state, not a separate host). MDI is a separate `MdiHostView` reading the *same* `Documents` collection, rendered via a custom `Canvas` + `MdiChildWindowControl` (drag/resize/close/maximize chrome, Cascade/Tile Horizontal/Tile Vertical). A global implicit `DataTemplate` in `App.xaml` maps `TailDocumentViewModel` → `TailDocumentView`, so every host — AvalonDock's `LayoutItemTemplate` and MDI's `ContentPresenter` alike — renders the identical view. Switching mode never recreates a `TailDocumentViewModel`, so scroll position, highlight state, bookmarks, and the live tail subscription survive untouched.

## Highlighting, Bookmarks, Navigation

`HighlightEngine` (Core) evaluates an ordered, priority-sorted `List<HighlightRule>` per appended line on the same background batch step as tailing. Phase 1 renders matches as **whole-line** foreground/background coloring (cheaper than per-substring spans, better for Remote Desktop).

Jump-to-highlight and bookmark navigation use `SortedSet<long>` indices of line numbers, giving O(log n) "next/previous relative to current position" lookups. Keyboard shortcuts (`F3`/`Shift+F3`, `Ctrl+F2`, `F2`/`Shift+F2`) are wired directly via WPF `InputBindings` bound to the active document's commands.

## Settings Persistence

`%LOCALAPPDATA%\LogViewer\settings.json` via `ISettingsStore`/`JsonSettingsStore` (`System.Text.Json`, injected path provider for testability). Persists global highlight rules, external tools, recent/open tail sources, window layout/mode (including bounds and the AvalonDock docking-layout XML as of Phase 4), ring buffer capacity, UI refresh interval, and a `SchemaVersion` (now 2) for forward-compatible migration.

## External Tools, Search, and Per-Document Customization (Phase 3)

`ExternalToolLauncher` (Core) substitutes `{FilePath}`/`{LineNumber}`/`{LineText}` into a tool's argument template and runs it via `Process.Start`, never throwing — failures surface through the same `StatusMessage` pattern as tailing errors. Tools are invoked manually (`TailDocumentView`'s "Run Tool" toolbar menu, or a per-tool `KeyBinding` rebuilt in `MainWindow` whenever the tool set changes) or automatically when a line matches a highlight rule a tool is configured to auto-trigger on (throttled to one launch per tool per 2 seconds so a burst of matching lines can't spawn a process storm).

Full-text search runs independently of the live tailing ring buffer, so it can find matches that were evicted or haven't been reached yet: `FileFullTextSearchService` streams a file from offset 0 reusing `LineSplitter`/`EncodingDetector`; `EventLogSearchService` scans a whole EventLog channel on a background thread (bridged to the async caller via an unbounded `Channel<T>`, since `EventLogReader` has no async API). Both are surfaced through a non-modal `SearchView` per document, so tailing keeps running while a search is in progress.

Per-document customization (`CustomColorHex`/`CustomIconGlyph` on `TailDocumentViewModel`) shows as a glyph prefix baked into `DisplayTitle` everywhere (tabs and MDI), plus a colored MDI title bar — AvalonDock's `LayoutItem` exposes no background/foreground styling surface for tab headers, so tab-level color-coding is MDI-mode only.

## Session Restore & Window-Chrome Persistence (Phase 4)

`TailSourceSettings` now carries a `TailSourceKind` (File/DirectoryWatch/EventLog) plus per-kind fields, per-document customization, and MDI bounds, so the same list backs both the "Recent Files" menu (filtered to `Kind == File`) and full session-restore — previously only plain files were ever reopened on startup; directory watches and EventLog sources were silently dropped. `MainViewModel.SaveAndDispose` syncs each open document's live customization/MDI bounds back into its settings entry before writing, and the last-active document is restored via `WindowLayoutSettings.ActiveSourceDedupKey`.

Main window bounds/maximize state and the AvalonDock docking layout (`XmlLayoutSerializer`, matched back to restored documents via `LayoutItem.ContentId == TailDocumentViewModel.SourcePath`) are captured in `MainWindow`'s `Closing` handler and reapplied on `Loaded`, clamped to the current virtual screen bounds so a saved position on a since-disconnected monitor can't strand the window off-screen.

Dropping a directory onto the main window (in addition to the existing file-drop-to-open) opens "Open Directory (Watch)" pre-filled with the dropped path.

`RemoteSessionDetector` (P/Invoke `GetSystemMetrics(SM_REMOTESESSION)`) widens the UI redraw-batching interval to a 250ms floor under a Remote Desktop session, via a pure/testable `EffectiveRefreshInterval` helper; togglable in Settings (`AutoTuneForRemoteDesktop`, on by default).

## MCP Server (Phase 5)

A third project, `LogViewer.Mcp` (plain `net10.0`, no WPF, `<FrameworkReference Include="Microsoft.AspNetCore.App" />`), hosts an embedded Kestrel server exposing the official `ModelContextProtocol`/`ModelContextProtocol.AspNetCore` C# SDK over **Streamable HTTP**, bound to `127.0.0.1:<configurable port>` (default `38173`). Streamable HTTP was chosen over stdio because the app is already a long-running GUI process with live state (open documents) — stdio would mean spawning a second, headless copy of the app per MCP client connection. `McpServerHost` (in `LogViewer.Mcp`) owns the `WebApplication`, is constructed in `App.xaml.cs OnStartup` (only when `AppSettings.Mcp.Enabled`, default `false`) sharing the same Core singleton instances the WPF app already resolved, and is stopped in `OnExit`; a port-bind failure is caught inside `McpServerHost.StartAsync` and surfaced via `MainViewModel.StatusMessage` rather than crashing startup.

Two new Core subsystems back the MCP tools, both UI-free and unit-tested like everything else in `LogViewer.Core`:
- `Analysis/` — `IPatternFrequencyAnalyzer`/`FilePatternFrequencyAnalyzer` aggregates a whole structured file into frequency tables, either by `MessageSignature` (recurring message *shapes*) or by a structured property's value (e.g. which `SourceContext` produced the most errors), with an `ExceptionFrameExtractor` fallback to the topmost stack frame when the call-site property is absent. `ILineWindowReader`/`FileLineWindowReader` reads a bounded window of raw lines around a line number. Both stream via the shared `StructuredFileReader` (extracted out of `FileBlockScanService`, which now reuses it too).
- `Documents/IOpenDocumentCatalog` — a Core-layer abstraction over "what documents are open right now", implemented in `LogViewer.App` by `WpfOpenDocumentCatalog` (projects `MainViewModel.Documents`) so the MCP tool layer never takes a WPF dependency.

`LogViewer.Mcp/Tools/` exposes 11 `[McpServerTool]` methods across four `[McpServerToolType]` classes (`LogDiscoveryTools`, `LogSearchTools`, `LogPatternTools`, `LogBlockTools`): listing open documents, describing/sampling a source, full-text search, line-context lookup, listing structured properties, top recurring patterns, top error sources by call site (the purpose-built "which functions keep erroring" tool), top values of any property, drilling into a pattern's occurrences, correlation/proximity block scanning, and similar-block finding — the last two wrapping the existing `IBlockScanService`/`ISimilarBlockFinder` block-diff engine. Every tool clamps its result count and truncates line text through `ResponseLimits`, independent of what the caller requests, so a broad query can't blow up the response payload.

`AppSettings.Mcp` (`McpServerSettings`: `Enabled`, `BindAddress`, `Port`, `MaxResultsPerCall`, `MaxLineTextLength`, `RequireApiKeyHeader`, `ApiKey`) is new in schema v5 (`JsonSettingsStore` migrates v4→v5 with no field-level work needed, same as the v2→v3 theme migration). The Settings dialog exposes an enable checkbox and port field (restart required to apply, noted in the UI).

## Verification

1. `dotnet build LogViewer.slnx -c Debug` from `C:\Dev\LogViewer` — all five projects (as of Phase 5: `LogViewer.Core`, `LogViewer.Mcp`, `LogViewer.App`, `LogViewer.Core.Tests`, `LogViewer.Mcp.Tests`) restore/compile.
2. `dotnet test tests\LogViewer.Core.Tests\LogViewer.Core.Tests.csproj` — 129/129 green (114 from Phases 1–4 + 15 from Phase 5: `FilePatternFrequencyAnalyzer` signature/property grouping, level filtering, `topN`, exception-frame fallback; `ExceptionFrameExtractor`; `FileLineWindowReader` windowing/clamping/out-of-range; `BlockLookup`), including truncation/rename/directory-auto-switch/EventLog-channel simulations against real temp files and the real Windows Application log.
3. `dotnet test tests\LogViewer.Mcp.Tests\LogViewer.Mcp.Tests.csproj` — 21/21 green, exercising every one of the 11 MCP tool methods directly against real fixture files (clamping/truncation, correlation vs. proximity block scanning, exception-frame fallback, error-level filtering), independent of the HTTP transport.
4. `dotnet run --project src\LogViewer.App\LogViewer.App.csproj`.
5. Phases 1–2 were manually verified end-to-end via screenshot-driven UI automation: growing-file live tailing with burst appends, highlight rule creation + live application, bookmark toggle/navigation, truncation/rotation reset-and-resume, tabbed multi-document, floating mode (independent OS windows), MDI mode (drag/resize/cascade/tile), directory-watch auto-switch, live EventLog tailing (including catching the app's own crash-log entry), Windows Services listing (316 real services), tray minimize/restore, tab file-change indicator, settings/session persistence across restart.
6. Phase 3/4 verification, by contrast, only had build/test/non-interactive-launch tooling available — confirmed via build, the test suite, and a background `dotnet run` smoke test (process starts cleanly, no crash/error output). **Still needs a manual UI pass**: external tool launch (manual + auto-trigger + shortcut gestures), search-dialog results and jump-to-line for both files and EventLog channels, the tab/MDI customize dialog, directory drag-drop, and window-bounds/AvalonDock-layout restore across a real restart.
7. Phase 5 likewise only had build/test/non-interactive-launch tooling available — confirmed via build, both test suites, and a background `dotnet run` smoke test with `Mcp.Enabled=true` (process starts cleanly, the configured port accepts a TCP connection, no crash/error output, and closing the app frees the port). **Still needs a manual pass with a real MCP client** (Claude Desktop/Code or the MCP Inspector) driving the actual tool-list/tool-call handshake over Streamable HTTP end-to-end, plus the Settings dialog's new checkbox/port field.
