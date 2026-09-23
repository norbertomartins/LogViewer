using System.Text;
using System.Text.RegularExpressions;
using LogViewer.Core.Structured;
using LogViewer.Core.Tailing;

namespace LogViewer.Core.Analysis;

/// <summary>One log line fed to an <see cref="ExceptionGrouper"/>.</summary>
public sealed record ExceptionScanLine(long LineNumber, string Text, StructuredLogEvent? Structured = null, DateTimeOffset? Timestamp = null);

/// <summary>Every occurrence of the "same" exception — same type and same top stack frames — collapsed into one row.</summary>
public sealed record ExceptionGroup(
    string Signature,
    string ExceptionType,
    string SampleMessage,
    string? TopFrame,
    int Count,
    long FirstLineNumber,
    long LastLineNumber,
    DateTimeOffset? FirstSeen,
    DateTimeOffset? LastSeen,
    string SampleText,
    IReadOnlyList<long> LineNumbers);

/// <summary>
/// Detects exceptions in a stream of log lines and groups identical ones. Handles:
/// <list type="bullet">
/// <item>structured events carrying an <see cref="StructuredLogEvent.Exception"/> (Serilog, JSON …);</item>
/// <item>plain-text .NET/Java traces — a <c>Namespace.SomeException: message</c> header followed by
/// indented <c>at …</c> frames (plus <c>Caused by:</c>, <c>--- End of …</c>, <c>... N more</c>);</item>
/// <item>Python tracebacks — <c>Traceback (most recent call last):</c>, <c>File "…", line N</c> frames, then the
/// <c>SomeError: message</c> line.</item>
/// </list>
/// A plain-text header only counts when at least one stack frame follows it, so a line that merely mentions
/// "TimeoutException" in prose isn't reported. The signature is the exception type plus the top
/// <see cref="SignatureFrameCount"/> frames with line numbers, file paths and compiler-generated digits stripped,
/// so the same failure from different builds/lines still groups together; without frames, the message with its
/// numbers/ids masked is used instead.
/// </summary>
public sealed partial class ExceptionGrouper
{
    public const int SignatureFrameCount = 3;
    public const int MaxLineNumbersPerGroup = 1000;
    private const int MaxSampleLines = 40;

    [GeneratedRegex(@"(?<type>(?:[A-Za-z_][\w$]*\.)*[A-Za-z_][\w$]*(?:Exception|Error|Fault))(?::\s*(?<msg>.*))?")]
    private static partial Regex HeaderPattern();

    [GeneratedRegex(@"^\s+at\s+(?<frame>.+)$")]
    private static partial Regex DotNetOrJavaFramePattern();

    [GeneratedRegex(@"^\s+File ""(?<file>[^""]+)"", line \d+(?:, in (?<func>.+))?")]
    private static partial Regex PythonFramePattern();

    [GeneratedRegex(@"^\s*(?:Caused by:|---\s|\.\.\.\s*\d+\s+more|--- End of)")]
    private static partial Regex TraceContinuationPattern();

    [GeneratedRegex(@"^Traceback \(most recent call last\):")]
    private static partial Regex PythonTracebackStartPattern();

    [GeneratedRegex(@"\s+in\s+.+?:line\s+\d+$|:line\s+\d+$|\((?<file>[^():]+?)(?::\d+)+\)$")]
    private static partial Regex FrameLocationPattern();

    [GeneratedRegex(@"\d+")]
    private static partial Regex DigitsPattern();

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}|\b0x[0-9a-fA-F]+\b|\d+")]
    private static partial Regex MessageVariablePattern();

    private readonly Dictionary<string, Accumulator> _groups = new(StringComparer.Ordinal);
    private PendingBlock? _block;

    public void Add(ExceptionScanLine line)
    {
        if (line.Structured?.Exception is { Length: > 0 } structuredException)
        {
            CloseBlock();
            AddStructured(line, structuredException);
            return;
        }

        if (_block is not null && TryContinueBlock(line))
        {
            return;
        }

        CloseBlock();
        TryOpenBlock(line);
    }

    public void AddRange(IEnumerable<ExceptionScanLine> lines)
    {
        foreach (var line in lines)
        {
            Add(line);
        }
    }

    /// <summary>Closes any trailing block and returns the groups, most frequent first (ties: most recent first).</summary>
    public IReadOnlyList<ExceptionGroup> Complete()
    {
        CloseBlock();
        return _groups.Values
            .Select(a => a.ToGroup())
            .OrderByDescending(g => g.Count)
            .ThenByDescending(g => g.LastLineNumber)
            .ToList();
    }

    public static IReadOnlyList<ExceptionGroup> Group(IEnumerable<ExceptionScanLine> lines)
    {
        var grouper = new ExceptionGrouper();
        grouper.AddRange(lines);
        return grouper.Complete();
    }

    /// <summary>Streams a whole file through a grouper — its structured format auto-detected, timestamps
    /// taken from the parsed event or else extracted from the raw line — without holding it in memory.</summary>
    public static async Task<IReadOnlyList<ExceptionGroup>> GroupFileAsync(string path, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var parser = LogLineParsers.Create(LogLineParsers.DetectFile(path));
            var grouper = new ExceptionGrouper();

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var (encoding, preambleLength) = EncodingDetector.Detect(stream);
            stream.Position = preambleLength;
            var splitter = new LineSplitter(encoding);
            var buffer = new byte[64 * 1024];
            long lineNumber = 0;
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var text in splitter.Append(buffer.AsSpan(0, read)))
                {
                    lineNumber++;
                    var structured = parser is not null && parser.TryParse(text, out var evt) ? evt : null;
                    grouper.Add(new ExceptionScanLine(lineNumber, text, structured, structured?.Timestamp ?? MergedTimestampExtractor.TryExtract(text)));
                }
            }

            return grouper.Complete();
        }, cancellationToken).ConfigureAwait(false);
    }

    private void AddStructured(ExceptionScanLine line, string exceptionText)
    {
        var exceptionLines = exceptionText.Replace("\r\n", "\n").Split('\n');
        var header = HeaderPattern().Match(exceptionLines[0]);
        var type = header.Success ? header.Groups["type"].Value : exceptionLines[0].Trim();
        var message = header.Success ? header.Groups["msg"].Value : string.Empty;
        var frames = exceptionLines.Skip(1)
            .Select(l => DotNetOrJavaFramePattern().Match(l))
            .Where(m => m.Success)
            .Select(m => m.Groups["frame"].Value.Trim())
            .ToList();

        Record(type, message, frames, line.LineNumber, line.Timestamp ?? line.Structured?.Timestamp, exceptionText);
    }

    private bool TryContinueBlock(ExceptionScanLine line)
    {
        var block = _block!;
        var text = line.Text;

        if (DotNetOrJavaFramePattern().Match(text) is { Success: true } frame)
        {
            block.Frames.Add(frame.Groups["frame"].Value.Trim());
            block.AppendSample(text);
            return true;
        }

        if (block.IsPython)
        {
            if (PythonFramePattern().Match(text) is { Success: true } pyFrame)
            {
                block.Frames.Add(pyFrame.Groups["func"].Success
                    ? $"{pyFrame.Groups["file"].Value} in {pyFrame.Groups["func"].Value}"
                    : pyFrame.Groups["file"].Value);
                block.AppendSample(text);
                return true;
            }

            if (text.Length > 0 && char.IsWhiteSpace(text[0]))
            {
                block.AppendSample(text); // source-code excerpt under a frame
                return true;
            }

            if (HeaderPattern().Match(text) is { Success: true } pyHeader && pyHeader.Index == 0)
            {
                block.Type = pyHeader.Groups["type"].Value;
                block.Message = pyHeader.Groups["msg"].Value;
                block.AppendSample(text);
                block.Frames.Reverse(); // Python lists the innermost call last
                CloseBlock();
                return true;
            }

            return false;
        }

        if (TraceContinuationPattern().IsMatch(text))
        {
            block.AppendSample(text);
            return true;
        }

        return false;
    }

    private void TryOpenBlock(ExceptionScanLine line)
    {
        if (PythonTracebackStartPattern().IsMatch(line.Text))
        {
            _block = new PendingBlock(line.LineNumber, line.Timestamp) { IsPython = true };
            _block.AppendSample(line.Text);
            return;
        }

        if (HeaderPattern().Match(line.Text) is { Success: true } header)
        {
            _block = new PendingBlock(line.LineNumber, line.Timestamp)
            {
                Type = header.Groups["type"].Value,
                Message = header.Groups["msg"].Value,
            };
            _block.AppendSample(line.Text);
        }
    }

    private void CloseBlock()
    {
        var block = _block;
        _block = null;
        if (block is null || block.Type is null || block.Frames.Count == 0)
        {
            return;
        }

        Record(block.Type, block.Message ?? string.Empty, block.Frames, block.LineNumber, block.Timestamp, block.Sample.ToString().TrimEnd());
    }

    private void Record(string type, string message, IReadOnlyList<string> frames, long lineNumber, DateTimeOffset? timestamp, string sampleText)
    {
        var normalizedFrames = frames.Take(SignatureFrameCount).Select(NormalizeFrame).ToList();
        var signature = normalizedFrames.Count > 0
            ? $"{type}|{string.Join("|", normalizedFrames)}"
            : $"{type}|{MessageVariablePattern().Replace(message, "#")}";

        if (!_groups.TryGetValue(signature, out var accumulator))
        {
            accumulator = new Accumulator(signature, type, message.Trim(), frames.Count > 0 ? frames[0] : null, lineNumber, timestamp, sampleText);
            _groups[signature] = accumulator;
        }

        accumulator.Add(lineNumber, timestamp);
    }

    private static string NormalizeFrame(string frame)
    {
        var withoutLocation = FrameLocationPattern().Replace(frame, m => m.Groups["file"].Success ? $"({m.Groups["file"].Value})" : string.Empty);
        return DigitsPattern().Replace(withoutLocation, "#").Trim();
    }

    private sealed class PendingBlock(long lineNumber, DateTimeOffset? timestamp)
    {
        private int _sampleLines;

        public long LineNumber { get; } = lineNumber;

        public DateTimeOffset? Timestamp { get; } = timestamp;

        public bool IsPython { get; init; }

        public string? Type { get; set; }

        public string? Message { get; set; }

        public List<string> Frames { get; } = [];

        public StringBuilder Sample { get; } = new();

        public void AppendSample(string text)
        {
            if (_sampleLines++ < MaxSampleLines)
            {
                Sample.AppendLine(text);
            }
        }
    }

    private sealed class Accumulator(string signature, string type, string message, string? topFrame, long firstLine, DateTimeOffset? firstSeen, string sample)
    {
        private readonly List<long> _lineNumbers = [];
        private readonly long _firstLine = firstLine;
        private int _count;
        private long _lastLine = firstLine;
        private DateTimeOffset? _firstSeen = firstSeen;
        private DateTimeOffset? _lastSeen = firstSeen;

        public void Add(long lineNumber, DateTimeOffset? timestamp)
        {
            _count++;
            _lastLine = lineNumber;
            if (_lineNumbers.Count < MaxLineNumbersPerGroup)
            {
                _lineNumbers.Add(lineNumber);
            }

            if (timestamp is { } ts)
            {
                _firstSeen ??= ts;
                _lastSeen = ts;
            }
        }

        public ExceptionGroup ToGroup() =>
            new(signature, type, message, topFrame, _count, _firstLine, _lastLine, _firstSeen, _lastSeen, sample, _lineNumbers);
    }
}
