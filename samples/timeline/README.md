# Timeline / merge sample logs

Two fake service logs covering the same 15-minute window (base `2026-02-15 09:00:00Z`),
with a deliberate volume burst around minutes 4–7 and an error storm around minute 9.

| File | Format | Use it to exercise |
|------|--------|--------------------|
| `orders-service.clef` | Serilog CLEF (JSON lines) | Structured view, volume timeline (per-level bars), colorization, error/exception detail panel |
| `payments-service.log` | plain text `yyyy-MM-dd HH:mm:ss.fff [LEVEL] message` | Volume timeline over a **non-structured** file (timestamp + level are parsed out of the raw line), highlight rules |

**Timeline:** open either file, then toggle the **📊 Timeline** button on the document toolbar.
Click a bar to jump to the first line in that time bucket. The mid-window burst and the
minute-9 error spike should be clearly visible.

**Merged view:** *File ▸ Open Merged Files (by time)…* and pick **both** files. Lines are
interleaved by timestamp and prefixed with `orders-service│` / `payments-service│`.
Structured view still works on the merged document (the prefix is stripped before parsing).

Regenerate (deterministic — byte-identical output):

```bash
python samples/timeline/generate.py
```
