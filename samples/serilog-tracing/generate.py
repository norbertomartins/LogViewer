#!/usr/bin/env python
"""Regenerates orders-api.clef — a fake SerilogTracing-instrumented service log used to exercise the
Trace Tree panel (LogViewer.Core.Structured.SerilogTraceTreeBuilder / LogViewer.App's Trace Tree window).

Emits real SerilogTracing-shaped CLEF: span-completion events carry native "@tr"/"@sp" trace/span ids
plus "ParentSpanId"/"SpanKind"/"SpanStartTimestamp" tags, and ordinary log lines emitted *during* a span
carry the same "TraceId"/"SpanId" (as SerilogTracing's ambient-Activity enrichment would add them) but no
SpanStartTimestamp, so the parser's IsSpan heuristic correctly treats them as plain correlated log lines,
not spans.

Deterministic: a fixed base timestamp and seeded RNG, so re-running produces byte-identical output.
Run from anywhere:  python samples/serilog-tracing/generate.py
"""
import json
import os
import random
from datetime import datetime, timedelta, timezone

HERE = os.path.dirname(os.path.abspath(__file__))
BASE = datetime(2026, 3, 1, 8, 0, 0, tzinfo=timezone.utc)

rng = random.Random(101)


def hex_id(nbytes):
    return "".join(rng.choice("0123456789abcdef") for _ in range(nbytes * 2))


def iso(t):
    return t.isoformat().replace("+00:00", "Z")


def span_event(t, name, trace_id, span_id, parent_span_id, kind, start, level="Information", tags=None, exception=None):
    evt = {"@t": iso(t), "@mt": name, "@l": level, "@tr": trace_id, "@sp": span_id}
    if exception:
        evt["@x"] = exception
    if parent_span_id:
        evt["ParentSpanId"] = parent_span_id
    evt["SpanKind"] = kind
    evt["SpanStartTimestamp"] = iso(start)
    if tags:
        evt.update(tags)
    return evt


def log_event(t, message, level, trace_id=None, span_id=None, tags=None):
    evt = {"@t": iso(t), "@mt": message, "@l": level}
    if trace_id:
        evt["TraceId"] = trace_id
    if span_id:
        evt["SpanId"] = span_id
    if tags:
        evt.update(tags)
    return evt


def gen_order_request(events, t0, order_id, *, warn_stock=False, db_error=False):
    """A GET /orders/{id} request: root Server span with a DB-query child span and an inventory-check
    HTTP client child span, plus one ordinary correlated log line — the shape a real ASP.NET Core +
    SerilogTracing app produces for one incoming request."""
    trace_id = hex_id(16)
    root_span = hex_id(8)
    db_span = hex_id(8)
    http_span = hex_id(8)

    root_start = t0
    events.append(log_event(t0 + timedelta(milliseconds=5), "Handling request for order {OrderId}", "Information",
                             trace_id, root_span, {"OrderId": order_id}))

    db_start = t0 + timedelta(milliseconds=20)
    db_duration = timedelta(milliseconds=180 if db_error else rng.randint(15, 90))
    db_level = "Error" if db_error else ("Warning" if warn_stock else "Information")
    db_tags = {"OrderId": order_id}
    db_exception = None
    if db_error:
        db_exception = ("System.Data.Common.DbException: connection timeout\n"
                         "   at Orders.Data.OrderRepository.GetAsync(Int64 orderId)")
    events.append(span_event(db_start + db_duration, "SELECT * FROM Orders WHERE Id = {OrderId}",
                              trace_id, db_span, root_span, "Client", db_start, db_level, db_tags, db_exception))

    http_start = db_start + db_duration + timedelta(milliseconds=5)
    http_duration = timedelta(milliseconds=rng.randint(10, 60))
    http_level = "Warning" if warn_stock else "Information"
    http_tags = {"OrderId": order_id, "Sku": f"SKU-{rng.randint(100, 999)}"}
    if warn_stock:
        http_tags["Remaining"] = rng.randint(0, 2)
    events.append(span_event(http_start + http_duration, "GET http://inventory/stock/{Sku}",
                              trace_id, http_span, root_span, "Client", http_start, http_level, http_tags))

    root_end = http_start + http_duration + timedelta(milliseconds=rng.randint(5, 25))
    root_level = "Error" if db_error else ("Warning" if warn_stock else "Information")
    events.append(span_event(root_end, "GET /orders/{OrderId}", trace_id, root_span, None, "Server",
                              root_start, root_level, {"OrderId": order_id}))
    return root_end


def gen_health_check(events, t0):
    trace_id = hex_id(16)
    span_id = hex_id(8)
    events.append(span_event(t0 + timedelta(milliseconds=3), "GET /health", trace_id, span_id, None, "Server", t0))
    return t0 + timedelta(milliseconds=3)


def gen_orphan_child(events, t0):
    """A DB span whose ParentSpanId points at a root span that fell outside the buffered/retained window —
    demonstrates SerilogTraceTreeBuilder surfacing it as its own root instead of dropping it."""
    trace_id = hex_id(16)
    missing_parent = hex_id(8)
    span_id = hex_id(8)
    start = t0
    end = t0 + timedelta(milliseconds=45)
    events.append(span_event(end, "SELECT * FROM Sessions WHERE Token = {Token}", trace_id, span_id,
                              missing_parent, "Client", start, "Information", {"Token": "***redacted***"}))
    return end


def gen_batch_job(events, t0, batch_id, item_ids, *, worker_rng, item_error_id=None):
    """A background worker's ProcessBatch: an Internal root span containing one Internal "ProcessItem" child
    span per item, each of which in turn has an Internal "SendEmail" grandchild span — a 3-level tree, unlike
    the flat 2-level HTTP-request traces in orders-api.clef. One item can fail (item_error_id), which should
    escalate only its own ProcessItem span (and the batch root) to Error, leaving sibling items untouched."""
    trace_id = hex_id(16)
    root_span = hex_id(8)
    root_start = t0
    cursor = t0 + timedelta(milliseconds=10)

    root_level = "Information"
    for item_id in item_ids:
        item_span = hex_id(8)
        item_start = cursor
        email_span = hex_id(8)
        email_start = item_start + timedelta(milliseconds=5)
        email_duration = timedelta(milliseconds=worker_rng.randint(20, 70))
        events.append(span_event(email_start + email_duration, "SendEmail {ItemId}", trace_id, email_span,
                                  item_span, "Internal", email_start, "Information",
                                  {"ItemId": item_id, "BatchId": batch_id}))

        item_end = email_start + email_duration + timedelta(milliseconds=worker_rng.randint(5, 15))
        item_level = "Error" if item_id == item_error_id else "Information"
        item_exception = None
        if item_id == item_error_id:
            item_exception = ("System.InvalidOperationException: item payload was malformed\n"
                               "   at Worker.Batch.ItemProcessor.ProcessAsync(String itemId)")
            root_level = "Error"
        events.append(span_event(item_end, "ProcessItem {ItemId}", trace_id, item_span, root_span, "Internal",
                                  item_start, item_level, {"ItemId": item_id, "BatchId": batch_id}, item_exception))
        cursor = item_end + timedelta(milliseconds=worker_rng.randint(5, 20))

    events.append(log_event(cursor, "Batch {BatchId} contained {ItemCount} items", "Information",
                             trace_id, root_span, {"BatchId": batch_id, "ItemCount": len(item_ids)}))
    root_end = cursor + timedelta(milliseconds=10)
    events.append(span_event(root_end, "ProcessBatch {BatchId}", trace_id, root_span, None, "Internal",
                              root_start, root_level, {"BatchId": batch_id}))
    return root_end


def gen_worker_batch_sample():
    """A second, independent sample: a queue-worker service processing batches, exercising 3-level span
    nesting (ProcessBatch > ProcessItem > SendEmail) and two traces overlapping in wall-clock time — neither
    shape appears in orders-api.clef, which is HTTP-request-shaped and strictly sequential."""
    worker_rng = random.Random(202)
    events = []
    t0 = datetime(2026, 3, 1, 9, 30, 0, tzinfo=timezone.utc)

    t_batch1_end = gen_batch_job(events, t0, "batch-1", ["item-1", "item-2", "item-3"], worker_rng=worker_rng)
    # batch-2 starts before batch-1 finishes, simulating two concurrent worker threads.
    t_batch2_start = t0 + timedelta(milliseconds=80)
    gen_batch_job(events, t_batch2_start, "batch-2", ["item-4", "item-5"], worker_rng=worker_rng,
                  item_error_id="item-5")
    gen_batch_job(events, t_batch1_end + timedelta(seconds=2), "batch-3", ["item-6"], worker_rng=worker_rng)

    events.sort(key=lambda e: e["@t"])
    lines = [json.dumps(e, separators=(",", ":")) for e in events]
    with open(os.path.join(HERE, "worker-batch.clef"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")
    print(f"wrote worker-batch.clef ({len(lines)} lines)")


def main():
    events = []
    t = BASE

    t = gen_order_request(events, t, 5001) + timedelta(seconds=2)
    t = gen_order_request(events, t, 5002) + timedelta(seconds=1)
    t = gen_health_check(events, t) + timedelta(seconds=1)
    t = gen_order_request(events, t, 5003, warn_stock=True) + timedelta(seconds=3)
    t = gen_order_request(events, t, 5004, db_error=True) + timedelta(seconds=2)
    t = gen_health_check(events, t) + timedelta(seconds=1)
    t = gen_orphan_child(events, t) + timedelta(seconds=2)
    t = gen_order_request(events, t, 5005) + timedelta(seconds=1)
    t = gen_order_request(events, t, 5006, warn_stock=True) + timedelta(seconds=2)
    t = gen_health_check(events, t)

    events.sort(key=lambda e: e["@t"])
    lines = [json.dumps(e, separators=(",", ":")) for e in events]
    with open(os.path.join(HERE, "orders-api.clef"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")
    print(f"wrote orders-api.clef ({len(lines)} lines)")

    gen_worker_batch_sample()


if __name__ == "__main__":
    main()
