#!/usr/bin/env python
"""Regenerates the sample log files used to exercise the volume timeline and merged tailing.

Deterministic: a fixed base timestamp and seeded RNG, so re-running produces byte-identical files.
Run from anywhere:  python samples/timeline/generate.py
"""
import json
import os
import random
from datetime import datetime, timedelta, timezone

HERE = os.path.dirname(os.path.abspath(__file__))
BASE = datetime(2026, 2, 15, 9, 0, 0, tzinfo=timezone.utc)

ORDER_MESSAGES = [
    ("Information", "Received order {OrderId} for customer {CustomerId}"),
    ("Information", "Order {OrderId} validated ({ItemCount} items, total {Total:C})"),
    ("Information", "Reserved stock for order {OrderId}"),
    ("Warning", "Stock low for SKU {Sku} ({Remaining} left) while processing order {OrderId}"),
    ("Information", "Order {OrderId} handed to fulfilment"),
    ("Error", "Failed to reserve stock for order {OrderId}: {Reason}"),
    ("Error", "Payment authorization declined for order {OrderId}"),
    ("Fatal", "Fulfilment queue unreachable - dropping order {OrderId}"),
]

PAYMENT_LEVELS = ["INFO", "INFO", "INFO", "DEBUG", "WARN", "ERROR"]
PAYMENT_MESSAGES = {
    "INFO": [
        "Authorizing payment {PaymentId} amount {Amount}",
        "Payment {PaymentId} captured",
        "Refund {PaymentId} processed",
        "Webhook delivered for {PaymentId}",
    ],
    "DEBUG": ["Gateway latency {Ms}ms for {PaymentId}", "Retrying {PaymentId} (attempt {Attempt})"],
    "WARN": ["Gateway slow: {Ms}ms for {PaymentId}", "Idempotency replay for {PaymentId}"],
    "ERROR": ["Gateway timeout for {PaymentId}", "Payment {PaymentId} rejected: {Reason}"],
}
REASONS = ["insufficient funds", "card expired", "risk hold", "gateway 503", "network reset"]


def render(template, props):
    out = template
    for k, v in props.items():
        if isinstance(v, (int, float)):
            out = out.replace("{" + k + ":C}", f"${v:,.2f}")
        out = out.replace("{" + k + "}", str(v))
    return out


def gen_orders(path):
    rng = random.Random(42)
    t = BASE
    lines = []
    for i in range(260):
        # Volume bursts: dense around minutes 4-7, an error storm around minute 9.
        minute = (t - BASE).total_seconds() / 60
        if 9 <= minute <= 10:
            weights = [1, 1, 1, 2, 1, 6, 5, 2]
        elif 4 <= minute <= 7:
            weights = [5, 5, 4, 2, 4, 1, 1, 0]
        else:
            weights = [4, 4, 3, 1, 3, 1, 1, 0]
        level, template = rng.choices(ORDER_MESSAGES, weights=weights)[0]
        order_id = 100000 + i
        props = {
            "OrderId": order_id,
            "CustomerId": f"c-{rng.randint(1000, 9999)}",
            "ItemCount": rng.randint(1, 8),
            "Total": round(rng.uniform(12, 480), 2),
            "Sku": f"SKU-{rng.randint(100, 999)}",
            "Remaining": rng.randint(0, 5),
            "Reason": rng.choice(REASONS),
        }
        evt = {
            "@t": t.isoformat().replace("+00:00", "Z"),
            "@mt": template,
            "@l": level,
        }
        if level in ("Error", "Fatal"):
            evt["@x"] = f"System.InvalidOperationException: {props['Reason']}\n   at Orders.Pipeline.Reserve(Int64 orderId)"
        for key in ("OrderId", "CustomerId", "ItemCount", "Total", "Sku", "Remaining", "Reason"):
            if "{" + key in template or "{" + key + ":C}" in template or key in ("OrderId",):
                evt[key] = props[key]
        lines.append(json.dumps(evt, separators=(",", ":")))
        t += timedelta(seconds=rng.randint(1, 9))
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")


def gen_payments(path):
    rng = random.Random(7)
    t = BASE + timedelta(seconds=3)
    lines = []
    for i in range(190):
        minute = (t - BASE).total_seconds() / 60
        level = rng.choice(PAYMENT_LEVELS)
        if 9 <= minute <= 10 and rng.random() < 0.5:
            level = "ERROR"
        template = rng.choice(PAYMENT_MESSAGES[level])
        props = {
            "PaymentId": f"pay_{rng.randint(100000, 999999)}",
            "Amount": f"${round(rng.uniform(5, 500), 2)}",
            "Ms": rng.randint(20, 2200),
            "Attempt": rng.randint(2, 4),
            "Reason": rng.choice(REASONS),
        }
        stamp = t.strftime("%Y-%m-%d %H:%M:%S.") + f"{t.microsecond // 1000:03d}"
        lines.append(f"{stamp} [{level}] {render(template, props)}")
        t += timedelta(seconds=rng.randint(1, 11))
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")


if __name__ == "__main__":
    gen_orders(os.path.join(HERE, "orders-service.clef"))
    gen_payments(os.path.join(HERE, "payments-service.log"))
    print("wrote orders-service.clef and payments-service.log")
