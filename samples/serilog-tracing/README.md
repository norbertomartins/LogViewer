# SerilogTracing sample logs

Two fake [SerilogTracing](https://github.com/serilog-tracing/serilog-tracing)-instrumented service logs,
used to exercise the app's Serilog span support end to end: `SerilogEventParser`'s `@tr`/`@sp` parsing,
the "⏱ duration" badge on structured rows, and the **Trace Tree** panel (`SerilogTraceTreeBuilder`).

## `orders-api.clef`

28 CLEF (JSON-lines) events covering 6 fake `GET /orders/{id}` requests, 3 `GET /health` checks, and one
deliberately "broken" span — the common HTTP request/response shape, strictly sequential, 2 levels deep.

| Scenario | What it shows |
|---|---|
| Requests for order 5001, 5002, 5005 | The common case: a `Server` root span (`GET /orders/{OrderId}`) with two `Client` child spans (a DB query, an inventory HTTP call) nested under it, plus one ordinary log line ("Handling request...") that carries the *same* `TraceId`/`SpanId` but isn't itself a span (no `SpanStartTimestamp`) — it should **not** show a duration badge or appear as its own node in the tree. |
| Order 5003, 5006 | The inventory-check child span (and the root span it rolls up into) logged at `Warning` — the Trace Tree's level color and the row-level `LevelToBrushConverter` should both show amber. |
| Order 5004 | The DB-query child span fails at `Error` with an attached exception (`@x`) — the root span also escalates to `Error`. Both should render red, and the exception should show in the line's detail panel. |
| `GET /health` × 3 | Single-span traces with no children — the Trace Tree should render a lone root node with no expand arrow. |
| The lone `SELECT * FROM Sessions...` span (between the two `GET /health` calls) | Its `ParentSpanId` points at a span id that isn't in the file (simulating a parent that fell outside the retained buffer) — `SerilogTraceTreeBuilder` should surface it as its **own** root rather than dropping it. |

## Try it

1. Open `orders-api.clef` in LogViewer and toggle **Structured View**. Span-completion rows show a
   "⏱ Xms" badge after the ThreadId column; the "Handling request..." lines don't.
2. Click the **🕸 Trace Tree** toolbar button (or Ctrl+P → "Show Trace Tree…"). The trace picker lists all
   10 traces, most recent first — pick one of the order-5001/5002/5005 traces to see the two-level span tree,
   or 5003/5004/5006 for the Warning/Error coloring.
3. Double-click a span node to jump straight to that line in the document.
4. Right-click any span row → **View Trace Tree** to open the panel preselected to that line's trace,
   instead of picking from the dropdown.
5. Right-click a span row → **Filter by TraceId** to isolate every line (span or plain log) belonging to
   that one request.

## `worker-batch.clef`

18 CLEF events from a fake queue-worker service, covering a shape `orders-api.clef` doesn't: **3-level**
span nesting (`ProcessBatch` → `ProcessItem` → `SendEmail`) and **two traces overlapping in wall-clock
time** (batch-2 starts before batch-1 finishes, as two concurrent worker threads would produce).

| Scenario | What it shows |
|---|---|
| `batch-1` (items 1–3) | A healthy 3-level tree: one `ProcessBatch` root, 3 `ProcessItem` children, each with its own `SendEmail` grandchild — confirms `SerilogTraceTreeBuilder` nests more than 2 levels deep. |
| `batch-2` (items 4–5) | `item-5`'s `ProcessItem` span fails at `Error` with an attached exception; the sibling `item-4` branch stays `Information` and the `ProcessBatch` root escalates to `Error` — only the failing branch turns red, not every sibling. |
| `batch-3` (item 6) | Starts ~2 seconds after `batch-1`/`batch-2`, non-overlapping — a plain single-item batch for contrast. |
| `batch-1` vs `batch-2` timestamps | Interleaved in the file (sorted by `@t`, not grouped by trace) — confirms the Trace Tree panel's per-`TraceId` filtering keeps concurrent traces separate instead of merging their spans. |

Try it the same way as `orders-api.clef` (toggle Structured View → 🕸 Trace Tree → pick `batch-1` or
`batch-2` from the trace picker to see the 3-level tree / partial-failure coloring).

## Regenerating

Deterministic (byte-identical output) — regenerates both files:

```bash
python samples/serilog-tracing/generate.py
```
