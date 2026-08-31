using LogViewer.Core.Tailing;

namespace LogViewer.Core.Structured;

/// <summary>
/// Central registry of every <see cref="ILogLineParser"/> and the format auto-detection that picks one
/// when a document is opened. Parsers are ordered most-specific-first (Serilog/CLEF before generic JSON)
/// so an ambiguous line is claimed by the tighter format.
/// </summary>
public static class LogLineParsers
{
    /// <summary>Format ids in detection-priority order.</summary>
    public static readonly IReadOnlyList<string> FormatIds = ["serilog", "ndjson", "w3c", "syslog", "logfmt"];

    private const int ReadBufferSize = 64 * 1024;

    /// <summary>Creates a fresh parser for <paramref name="formatId"/>, or null if unknown. A new instance is
    /// returned every call because some parsers (W3C) are stateful and must not be shared between documents.</summary>
    public static ILogLineParser? Create(string? formatId) => formatId switch
    {
        "serilog" => new SerilogLogLineParser(),
        "ndjson" => new GenericJsonLogLineParser(),
        "w3c" => new W3cExtendedLogLineParser(),
        "syslog" => new SyslogLogLineParser(),
        "logfmt" => new LogfmtLogLineParser(),
        _ => null,
    };

    /// <summary>All parsers, fresh instances, in detection-priority order.</summary>
    public static IReadOnlyList<ILogLineParser> CreateAll() => [.. FormatIds.Select(id => Create(id)!)];

    /// <summary>
    /// Returns the <see cref="ILogLineParser.FormatId"/> of the format that best explains
    /// <paramref name="sampleLines"/> (fed in file order), or null when no format parses at least
    /// <paramref name="threshold"/> of the non-blank sample lines with a minimum of <paramref name="minSamples"/>.
    /// </summary>
    public static string? Detect(IEnumerable<string> sampleLines, double threshold = 0.75, int minSamples = 2)
    {
        var samples = sampleLines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (samples.Count == 0)
        {
            return null;
        }

        string? bestId = null;
        var bestScore = 0.0;

        foreach (var parser in CreateAll())
        {
            var considered = 0;
            var matched = 0;

            foreach (var line in samples)
            {
                // A directive line the parser consumes for state (e.g. W3C "#Fields:") returns false but
                // must not count against it.
                var isEvent = parser.TryParse(line, out _);
                if (!isEvent && line.StartsWith('#'))
                {
                    continue;
                }

                considered++;
                if (isEvent)
                {
                    matched++;
                }
            }

            if (considered < minSamples)
            {
                continue;
            }

            var score = (double)matched / considered;
            if (score >= threshold && score > bestScore)
            {
                bestScore = score;
                bestId = parser.FormatId;
            }
        }

        return bestId;
    }

    /// <summary>Reads up to <paramref name="maxLines"/> lines from the start of <paramref name="path"/> and runs
    /// <see cref="Detect(IEnumerable{string}, double, int)"/> over them. Returns null if the file can't be read.</summary>
    public static string? DetectFile(string path, int maxLines = 30)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var (encoding, preambleLength) = EncodingDetector.Detect(stream);
            stream.Position = preambleLength;

            var splitter = new LineSplitter(encoding);
            var buffer = new byte[ReadBufferSize];
            var sample = new List<string>(maxLines);

            int read;
            while (sample.Count < maxLines && (read = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                foreach (var line in splitter.Append(buffer.AsSpan(0, read)))
                {
                    sample.Add(line);
                    if (sample.Count >= maxLines)
                    {
                        break;
                    }
                }
            }

            return Detect(sample);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
