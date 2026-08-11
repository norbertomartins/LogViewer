# Find Similar Block — sample log pairs

Each folder below is a `v1.log`/`v2.log` pair simulating the same operation logged by two different
app versions, for trying out the "Find Similar Block In..." context-menu command (right-click a
structured log line). Open both files as documents, switch on "Structured View" for each, then
right-click a line in `v1.log` and pick "Find Similar Block In..." with `v2.log`'s document (or a
browsed path to it) as the target.

## 01-correlation-template

Serilog CLEF events with an explicit `MessageTemplate`, correlated by `TraceId`. `v2.log` adds a
"Payment gateway timeout, retrying" warning line not present in `v1.log`, and the final
`Order {OrderId} completed in {DurationMs}ms` line has a much larger duration.

Suggested correlation field: `TraceId` (auto-suggested). Expect: one only-in-target row (the retry
warning), one common-but-values-differ row (the completion line's `DurationMs`).

## 02-no-template-masking

Events with only a rendered message (`@m`, no `@mt`) — variable values (IP address, a job id, a
duration) are baked directly into the text, so matching relies entirely on `MessageSignature`'s
masking rather than a template. Correlated by `JobId`. `v2.log` adds a
"Database connection refused, retrying" line and a much slower "Cache warmed..." step.

Suggested correlation field: `JobId`. Expect: masked lines like `Connecting to database at 10.0.0.5:5432`
and `10.0.0.9:5432` to align as Common (same shape, `ValuesDiffer` true since the raw text differs),
plus one only-in-target retry row.

## 03-proximity-fallback

No correlation-id property at all — clear the correlation-field box in the dialog so it falls back to
line/time/thread proximity. Both files log a "Flush batch" operation on `ThreadId=4`; `v2.log` adds a
"Lock contention detected, waiting" line and a slower completion, followed by an unrelated `Heartbeat`
line on a different thread far later (should NOT be pulled into the block).

## 04-standard-json-formatter

Same idea as (01) but using Serilog's standard `JsonFormatter` shape (`Timestamp`/`Level`/
`MessageTemplate`/`Properties`) instead of the compact CLEF shape, to exercise the other branch of the
parser. Correlated by `CorrelationId`. `v2.log` adds an "MFA challenge sent to {Username}" step and a
much slower login.
