namespace LogViewer.Core.Analysis;

/// <summary>One row of a "top recurring message patterns" analysis: every log line whose
/// <see cref="BlockDiff.MessageSignature"/> matched this one, collapsed into a single count.</summary>
public sealed record PatternFrequencyEntry(
    string Signature,
    string? Level,
    string SampleMessage,
    int Count,
    long FirstLineNumber,
    DateTimeOffset? FirstTimestamp,
    long LastLineNumber,
    DateTimeOffset? LastTimestamp,
    IReadOnlyList<long> SampleLineNumbers);

/// <summary>One row of a "top values of a structured property" analysis (e.g. which <c>SourceContext</c>
/// produced the most Error-level lines) — <see cref="DistinctSignatureCount"/> tells the caller whether
/// this value covers one specific log statement or many different ones.</summary>
public sealed record PropertyFrequencyEntry(
    string PropertyValue,
    int Count,
    int DistinctSignatureCount,
    long FirstLineNumber,
    DateTimeOffset? FirstTimestamp,
    long LastLineNumber,
    DateTimeOffset? LastTimestamp,
    IReadOnlyList<string> SamplePatternSignatures);
