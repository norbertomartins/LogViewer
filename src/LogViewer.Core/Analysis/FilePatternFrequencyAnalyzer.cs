using LogViewer.Core.BlockDiff;
using LogViewer.Core.Structured;

namespace LogViewer.Core.Analysis;

public sealed class FilePatternFrequencyAnalyzer : IPatternFrequencyAnalyzer
{
    // Memory bound on distinct groups tracked, in the same spirit as BlockDetectionOptions.MaxTrackedGroups:
    // once exceeded, newly-seen distinct keys are no longer tracked (existing ones keep accumulating), so a
    // pathologically wide file (e.g. unmasked free text with embedded ids) stays bounded at the cost of
    // possibly missing a late-appearing high-frequency group in that edge case.
    private const int MaxTrackedKeys = 20_000;
    private const int MaxSamplesPerKey = 3;

    public async Task<IReadOnlyList<PatternFrequencyEntry>> AnalyzeBySignatureAsync(
        string sourcePath, string? minLevel, int topN, CancellationToken cancellationToken)
    {
        var minRank = LogLevelSeverity.Rank(minLevel);
        var accumulators = new Dictionary<string, SignatureAccumulator>(StringComparer.Ordinal);

        await foreach (var (lineNumber, evt) in StructuredFileReader.ReadAsync(sourcePath, cancellationToken).ConfigureAwait(false))
        {
            if (minRank is not null && (LogLevelSeverity.Rank(evt.Level) ?? -1) < minRank)
            {
                continue;
            }

            var signature = MessageSignature.Compute(evt);
            if (!accumulators.TryGetValue(signature, out var acc))
            {
                if (accumulators.Count >= MaxTrackedKeys)
                {
                    continue;
                }

                acc = new SignatureAccumulator(signature, evt.Level, evt.RenderedMessage);
                accumulators[signature] = acc;
            }

            acc.Add(lineNumber, evt.Timestamp);
        }

        return accumulators.Values
            .Select(a => a.ToEntry())
            .OrderByDescending(e => e.Count)
            .Take(Math.Max(0, topN))
            .ToList();
    }

    public async Task<IReadOnlyList<PropertyFrequencyEntry>> AnalyzeByPropertyAsync(
        string sourcePath, string propertyName, string? minLevel, bool useExceptionFrameFallback, int topN, CancellationToken cancellationToken)
    {
        var minRank = LogLevelSeverity.Rank(minLevel);
        var accumulators = new Dictionary<string, PropertyAccumulator>(StringComparer.Ordinal);

        await foreach (var (lineNumber, evt) in StructuredFileReader.ReadAsync(sourcePath, cancellationToken).ConfigureAwait(false))
        {
            if (minRank is not null && (LogLevelSeverity.Rank(evt.Level) ?? -1) < minRank)
            {
                continue;
            }

            var value = StructuredFieldResolver.Resolve(evt, propertyName);
            if (string.IsNullOrEmpty(value) && useExceptionFrameFallback)
            {
                value = ExceptionFrameExtractor.ExtractTopFrame(evt.Exception);
            }

            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            if (!accumulators.TryGetValue(value, out var acc))
            {
                if (accumulators.Count >= MaxTrackedKeys)
                {
                    continue;
                }

                acc = new PropertyAccumulator(value);
                accumulators[value] = acc;
            }

            acc.Add(lineNumber, evt.Timestamp, MessageSignature.Compute(evt));
        }

        return accumulators.Values
            .Select(a => a.ToEntry())
            .OrderByDescending(e => e.Count)
            .Take(Math.Max(0, topN))
            .ToList();
    }

    private sealed class SignatureAccumulator(string signature, string? level, string sampleMessage)
    {
        private readonly List<long> _sampleLineNumbers = [];

        public int Count { get; private set; }

        public long FirstLineNumber { get; private set; }

        public DateTimeOffset? FirstTimestamp { get; private set; }

        public long LastLineNumber { get; private set; }

        public DateTimeOffset? LastTimestamp { get; private set; }

        public void Add(long lineNumber, DateTimeOffset? timestamp)
        {
            if (Count == 0)
            {
                FirstLineNumber = lineNumber;
                FirstTimestamp = timestamp;
            }

            Count++;
            LastLineNumber = lineNumber;
            LastTimestamp = timestamp;

            if (_sampleLineNumbers.Count < MaxSamplesPerKey)
            {
                _sampleLineNumbers.Add(lineNumber);
            }
        }

        public PatternFrequencyEntry ToEntry() => new(
            signature, level, sampleMessage, Count, FirstLineNumber, FirstTimestamp, LastLineNumber, LastTimestamp, _sampleLineNumbers);
    }

    private sealed class PropertyAccumulator(string value)
    {
        private readonly List<string> _sampleSignatures = [];
        private readonly HashSet<string> _distinctSignatures = new(StringComparer.Ordinal);

        public int Count { get; private set; }

        public long FirstLineNumber { get; private set; }

        public DateTimeOffset? FirstTimestamp { get; private set; }

        public long LastLineNumber { get; private set; }

        public DateTimeOffset? LastTimestamp { get; private set; }

        public void Add(long lineNumber, DateTimeOffset? timestamp, string signature)
        {
            if (Count == 0)
            {
                FirstLineNumber = lineNumber;
                FirstTimestamp = timestamp;
            }

            Count++;
            LastLineNumber = lineNumber;
            LastTimestamp = timestamp;

            if (_distinctSignatures.Add(signature) && _sampleSignatures.Count < MaxSamplesPerKey)
            {
                _sampleSignatures.Add(signature);
            }
        }

        public PropertyFrequencyEntry ToEntry() => new(
            value, Count, _distinctSignatures.Count, FirstLineNumber, FirstTimestamp, LastLineNumber, LastTimestamp, _sampleSignatures);
    }
}
