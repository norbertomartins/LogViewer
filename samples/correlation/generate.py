"""Regenerates checkout-service.log: a deterministic plain-text service log for exercising the
correlation-id filter, time navigation and the Exceptions panel (see ../../README.md features).

- one INFO line per second from 10:00:00, each carrying request_id=req-1xx (7 rotating ids);
- every 25th request fails: an ERROR line with the same request_id followed by a .NET stack trace
  (untimestamped continuation lines), alternating between two distinct exception types.
"""
import os

lines = []
for i in range(180):
    ts = f"2026-09-23 10:{i // 60:02d}:{i % 60:02d}"
    rid = f"req-{100 + i % 7}"
    lines.append(f"{ts}.000 [INFO] handled GET /orders request_id={rid} in {20 + i % 9}ms")
    if i % 25 == 24:
        lines.append(f"{ts}.500 [ERROR] checkout failed request_id={rid}")
        if (i // 25) % 2 == 0:
            lines += ["System.InvalidOperationException: Order not found",
                      "   at Shop.Orders.Load(Int32 id)",
                      "   at Shop.Api.Get()"]
        else:
            lines += ["System.TimeoutException: Payment gateway did not answer",
                      "   at Shop.Payments.Charge(Decimal amount)",
                      "   at Shop.Api.Checkout()"]

path = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'checkout-service.log')
with open(path, 'w', newline='\n', encoding='utf-8') as f:
    f.write('\n'.join(lines) + '\n')
print(path, len(lines))
