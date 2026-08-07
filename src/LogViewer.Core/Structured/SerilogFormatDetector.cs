using LogViewer.Core.Tailing;

namespace LogViewer.Core.Structured;

/// <summary>Sniffs whether a file looks like Serilog JSON output, for auto-selecting structured view on open.
/// Reads only the first few non-blank lines from the start of the file — independent of <see cref="FileTailSource"/>'s
/// tail-from-EOF initial read, so it never affects how large files are opened.</summary>
public static class SerilogFormatDetector
{
    private const int ReadBufferSize = 64 * 1024;

    /// <summary>True when at least <paramref name="minSamples"/> non-blank lines were seen and at least
    /// <paramref name="threshold"/> of them parse as Serilog JSON.</summary>
    public static bool LooksLikeSerilogJson(IEnumerable<string> sampleLines, double threshold = 0.8, int minSamples = 3)
    {
        var total = 0;
        var matched = 0;

        foreach (var line in sampleLines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            total++;
            if (SerilogEventParser.TryParse(line, out _))
            {
                matched++;
            }
        }

        return total >= minSamples && (double)matched / total >= threshold;
    }

    /// <summary>Reads up to <paramref name="maxLines"/> non-blank lines from the start of <paramref name="path"/>
    /// and evaluates them via <see cref="LooksLikeSerilogJson"/>. Returns false if the file can't be read.</summary>
    public static bool SniffFile(string path, int maxLines = 20)
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

            return LooksLikeSerilogJson(sample);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
