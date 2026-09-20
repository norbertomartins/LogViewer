using System.Globalization;

namespace LogViewer.Core.Structured;

/// <summary>A single log event parsed out of a Serilog JSON line (CLEF or the standard <c>JsonFormatter</c> shape).
/// <paramref name="TraceId"/>/<paramref name="SpanId"/> are Serilog's native <c>Activity</c>-based trace fields
/// (CLEF's <c>@tr</c>/<c>@sp</c>, or the standard formatter's top-level <c>TraceId</c>/<c>SpanId</c>) — the ones
/// <see href="https://github.com/serilog-tracing/serilog-tracing">SerilogTracing</see> spans carry, distinct from
/// any plain <c>TraceId</c>/<c>SpanId</c> property an enricher might add under <see cref="Properties"/>.</summary>
public sealed record StructuredLogEvent(
    DateTimeOffset? Timestamp,
    string? Level,
    string? MessageTemplate,
    string RenderedMessage,
    string? Exception,
    IReadOnlyDictionary<string, string> Properties,
    string? TraceId = null,
    string? SpanId = null)
{
    /// <summary>The completed span's parent, from the <c>ParentSpanId</c> tag SerilogTracing adds to every span event.</summary>
    public string? ParentSpanId => Properties.TryGetValue("ParentSpanId", out var v) ? v : null;

    /// <summary>SerilogTracing's span classification tag (e.g. "Internal", "Client", "Server").</summary>
    public string? SpanKind => Properties.TryGetValue("SpanKind", out var v) ? v : null;

    /// <summary>The span's start time, from SerilogTracing's <c>SpanStartTimestamp</c> tag — <see cref="Timestamp"/>
    /// itself is when the span *completed*.</summary>
    public DateTimeOffset? SpanStartTimestamp =>
        Properties.TryGetValue("SpanStartTimestamp", out var v)
        && DateTimeOffset.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var ts)
            ? ts
            : null;

    /// <summary>True when this event is a SerilogTracing span-completion event rather than an ordinary log line —
    /// per SerilogTracing's own guidance, that's a <see cref="TraceId"/> + <see cref="SpanId"/> pair together with
    /// a <see cref="SpanStartTimestamp"/> (an ordinary log line emitted while a span is active carries the same
    /// TraceId/SpanId via <c>Activity.Current</c>, but never a start timestamp).</summary>
    public bool IsSpan => TraceId is not null && SpanId is not null && SpanStartTimestamp is not null;

    /// <summary>Wall-clock duration of the span, or null when this isn't a span event or <see cref="Timestamp"/> is missing.</summary>
    public TimeSpan? SpanDuration => IsSpan && Timestamp is not null ? Timestamp.Value - SpanStartTimestamp!.Value : null;
}
