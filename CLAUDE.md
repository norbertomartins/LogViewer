# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

LogViewer is a WPF (.NET 10, Windows-only) tail utility for text logs and Windows Event Logs — live
tailing, regex highlighting, bookmarks, directory/wildcard watching, full-file search, block-diff/
similarity analysis across log versions, and an optional embedded MCP server so an AI agent can query
the logs the app is tailing. See `PLAN.md` for the full phase-by-phase architecture writeup and design
rationale — it is the authoritative design doc; consult it before making non-trivial changes, and keep
it updated when you land a new phase/feature of similar scope.

## Solution layout

```
src/LogViewer.Core/    net10.0, no WPF/UI references — the testable engine
src/LogViewer.Mcp/     net10.0, no WPF, FrameworkReference Microsoft.AspNetCore.App — embedded MCP server
src/LogViewer.App/     net10.0-windows, UseWPF — the WPF/MVVM shell
tests/LogViewer.Core.Tests/   xUnit, net10.0 — Core unit tests
tests/LogViewer.Mcp.Tests/    xUnit, net10.0 — MCP tool unit tests (bypass HTTP transport)
tests/LogViewer.App.Tests/    xUnit, net10.0-windows, UseWPF — ViewModel tests
tests/LogViewer.UITests/      xUnit, net10.0-windows — FlaUI UI-automation tests, drive the built .exe out-of-process
benchmarks/LogViewer.Benchmarks/  BenchmarkDotNet micro-benchmarks
samples/block-diff/           fixture log pairs for the block-diff/similarity engine
```

`LogViewer.Core` has zero WPF dependency by design — anything reusable by both the WPF app and the MCP
server belongs there. `LogViewer.App`'s `Services/WpfOpenDocumentCatalog.cs` etc. are the pattern for
bridging a Core abstraction into WPF without leaking WPF types back into Core.

## Build/test environment note

**This solution targets `net10.0-windows` for `LogViewer.App`, `LogViewer.App.Tests`, and
`LogViewer.UITests`, and requires WPF/Windows to build and run those three projects.** On a non-Windows
dev box (e.g. this Linux container), only `LogViewer.Core`, `LogViewer.Mcp`, `LogViewer.Core.Tests`, and
`LogViewer.Mcp.Tests` (all plain `net10.0`) will build and run. CI (`.github/workflows/build.yml`) runs
on `windows-latest` and only tests those same two cross-platform suites; it does not run
`LogViewer.App.Tests` or `LogViewer.UITests` at all.

## Commands

```bash
# Restore/build everything (Windows only for the full solution)
dotnet restore LogViewer.slnx
dotnet build LogViewer.slnx -c Debug

# Build/test only the cross-platform projects (works on Linux/macOS too)
dotnet build src/LogViewer.Core/LogViewer.Core.csproj
dotnet build src/LogViewer.Mcp/LogViewer.Mcp.csproj
dotnet test tests/LogViewer.Core.Tests/LogViewer.Core.Tests.csproj
dotnet test tests/LogViewer.Mcp.Tests/LogViewer.Mcp.Tests.csproj

# Single test (xUnit filter, works with any of the four test projects)
dotnet test tests/LogViewer.Core.Tests/LogViewer.Core.Tests.csproj --filter "FullyQualifiedName~FileTailSourceTests.SomeTestName"

# Windows-only: WPF app + its ViewModel/UI test suites
dotnet test tests/LogViewer.App.Tests/LogViewer.App.Tests.csproj
dotnet test tests/LogViewer.UITests/LogViewer.UITests.csproj   # drives a built LogViewer.App.exe via FlaUI, out-of-process
dotnet run --project src/LogViewer.App/LogViewer.App.csproj

# Release single-file publish (what CI ships)
dotnet publish src/LogViewer.App/LogViewer.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish

# Benchmarks
dotnet run -c Release --project benchmarks/LogViewer.Benchmarks/LogViewer.Benchmarks.csproj
```

Shared build settings (`LangVersion=latest`, `Nullable=enable`, `ImplicitUsings=enable`, .NET analyzers on)
live in `Directory.Build.props` and apply to every project. `global.json` pins the SDK to `10.0.302` with
`rollForward: latestFeature`.

## Code conventions (`.editorconfig`)

- File-scoped namespaces, braces required, `System.*` usings sorted first.
- 4-space indent for C#, CRLF line endings, final newline required, trailing whitespace trimmed.
- No `this.` qualification for fields/properties/methods; `var` when the type is apparent from the RHS.

## Core architectural patterns

**`ITailSource`** (Core) is the abstraction every log source implements — `FileTailSource`,
`DirectoryWatchTailSource`, `WindowsEventLogSource` — exposing **batched** events (`LinesRead` per
read-cycle, never per-line; `SourceReset` for truncation/rotation; `Error`), so `TailDocumentViewModel`
hosts any of them identically and downstream UI never processes one line at a time under load. When
adding a new source kind, implement `ITailSource` and wire it into `TailSourceKind`/`TailSourceSettings`
for persistence — don't special-case it in the ViewModel layer.

**`TailDocumentViewModel`** is the single document view-model shared across all three window-hosting
modes (Tabbed/Floating via AvalonDock, and a hand-built MDI `Canvas` host — WPF has no native/maintained
MDI control). Switching window mode never recreates this ViewModel, so scroll position, highlights,
bookmarks, and the live tail subscription survive the switch untouched. A single implicit `DataTemplate`
in `App.xaml` maps it to `TailDocumentView` so every host renders the same view.

**Reset-before-content ordering matters**: any composite/forwarding tail source (see
`DirectoryWatchTailSource`) must raise `SourceReset` *before* the new underlying source starts delivering
lines — consumers clear their display on reset, so firing it after `Start()` silently wipes out content
that was already delivered. This was a real Phase 2 bug; keep the regression test that asserts event
order intact if you touch this code.

**UI throughput bridge**: background-thread tail events are queued (`UiDispatcherLineSink`) and drained
on a single throttled dispatch (~50-100ms), never per-event — `RingLineBuffer` is deliberately not an
`ObservableCollection` for the same reason (no per-item `CollectionChanged`). Any new high-frequency
event path (new source kind, new analysis feature) should follow this same batch-and-throttle shape
rather than pushing straight to WPF bindings.

**Settings/session persistence**: `ISettingsStore`/`JsonSettingsStore` writes
`%LOCALAPPDATA%\LogViewer\settings.json` with a `SchemaVersion` field. Adding a persisted field requires
bumping `SchemaVersion` and adding a migration step in `JsonSettingsStore` (see the v2→v3 and v4→v5
migrations for the pattern) — never silently change the shape of an existing schema version.

**MCP server** (`LogViewer.Mcp`, Phase 5): an embedded Kestrel host over Streamable HTTP (not stdio,
since the app is a long-running GUI process with live state), started from `App.xaml.cs OnStartup` only
when `AppSettings.Mcp.Enabled` (default `false`), sharing the same Core singletons the WPF app already
resolved. MCP tools (`LogViewer.Mcp/Tools/*Tools.cs`, `[McpServerTool]`/`[McpServerToolType]`) must stay
UI-free — they depend on Core abstractions like `IOpenDocumentCatalog`, never on WPF types directly (see
`WpfOpenDocumentCatalog` for how App wires the WPF-side implementation in). Every tool clamps result
count/line length via `ResponseLimits` regardless of what the caller requests — follow that pattern for
any new tool so a broad query can't blow up the response payload.

## Testing conventions

- `LogViewer.Core.Tests` and `LogViewer.Mcp.Tests` use real temp files/fixtures (see each project's
  `TestUtilities/TempFileFixture.cs`) rather than mocking the filesystem — tailing/truncation/rotation
  behavior is verified against actual file I/O.
- `LogViewer.App.Tests` uses NSubstitute for mocking and exercises ViewModels in-process.
- `LogViewer.UITests` has **no** `ProjectReference` to `LogViewer.App` by design — it drives the already
  published `LogViewer.App.exe` out-of-process via FlaUI/UI Automation, the same way a real user would.
- `samples/block-diff/*/v1.log` + `v2.log` pairs are fixtures for the block-diff/similarity engine
  (`IBlockScanService`, `SimilarBlockFinder`) covering correlation-template matching, no-template masking,
  proximity fallback, and structured JSON formatting — add a new numbered subfolder there when adding a
  new block-diff scenario, matching the existing naming pattern.
