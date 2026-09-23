namespace LogViewer.Core.Analysis;

/// <summary>Where <see cref="MultiLineEntryTracker{TLine}.Add"/> placed a line.</summary>
/// <param name="Head">The entry the line continues, or null when it starts an entry of its own.</param>
/// <param name="AdoptedHeader">An earlier line — a bare exception header such as "System.IO.IOException: …" printed on
/// its own line under the log message — that turned out to be part of <paramref name="Head"/>'s entry too, now that a
/// stack frame followed it; null otherwise.</param>
public readonly record struct EntryPlacement<TLine>(TLine? Head, TLine? AdoptedHeader)
    where TLine : class;

/// <summary>
/// Streams lines in order and groups stack traces with the log entry they belong to, recognising the same shapes as
/// <see cref="ExceptionGrouper"/>: indented .NET/Java <c>at …</c> frames (plus <c>Caused by:</c>, <c>---&gt;</c>,
/// <c>--- End of …</c>, <c>... N more</c> and other indented lines once a trace has started) and Python tracebacks
/// (<c>Traceback (most recent call last):</c>, <c>File "…"</c> frames, the source excerpts under them and the closing
/// <c>SomeError: …</c> line). Any other line starts a new entry; a blank line ends the current one.
/// </summary>
public sealed class MultiLineEntryTracker<TLine>
    where TLine : class
{
    private TLine? _head;
    private TLine? _previousHead;
    private bool _headIsBareHeader;
    private bool _inTrace;
    private bool _inPythonTrace;

    public EntryPlacement<TLine> Add(TLine line, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            Reset();
            return default;
        }

        if (_head is not null && ContinuesEntry(text, out var startsTrace))
        {
            TLine? adopted = null;
            if (startsTrace && _headIsBareHeader && _previousHead is not null)
            {
                // "ERROR Payment failed" / "System.TimeoutException: …" / "   at …": the header line is part of the
                // entry above, not an entry of its own.
                adopted = _head;
                _head = _previousHead;
            }

            _headIsBareHeader = false;
            _previousHead = null;
            return new EntryPlacement<TLine>(_head, adopted);
        }

        StartEntry(line, text);
        return default;
    }

    /// <summary>Makes <paramref name="line"/> the head of a new entry without classifying it — for lines a parser already
    /// recognised as entries of their own (structured events).</summary>
    public void StartEntry(TLine line, string text)
    {
        _previousHead = _head;
        _head = line;
        _headIsBareHeader = _previousHead is not null && ExceptionGrouper.IsHeaderAtStart(text);
        _inTrace = false;
        _inPythonTrace = ExceptionGrouper.IsPythonTracebackStart(text);
    }

    public void Reset()
    {
        _head = null;
        _previousHead = null;
        _headIsBareHeader = false;
        _inTrace = false;
        _inPythonTrace = false;
    }

    private bool ContinuesEntry(string text, out bool startsTrace)
    {
        startsTrace = !_inTrace && !_inPythonTrace;

        if (_inPythonTrace)
        {
            if (ExceptionGrouper.IsPythonFrameLine(text) || char.IsWhiteSpace(text[0]))
            {
                return true;
            }

            if (ExceptionGrouper.IsHeaderAtStart(text))
            {
                // The closing "ValueError: …" line ends the traceback.
                _inPythonTrace = false;
                _inTrace = false;
                return true;
            }

            return false;
        }

        if (ExceptionGrouper.IsFrameLine(text))
        {
            _inTrace = true;
            return true;
        }

        if (ExceptionGrouper.IsPythonTracebackStart(text))
        {
            _inPythonTrace = true;
            return true;
        }

        // Once a trace has started: "Caused by: …", " ---> Inner…", "--- End of …", "... 12 more", and any indented line.
        if (_inTrace && (ExceptionGrouper.IsTraceContinuationLine(text) || char.IsWhiteSpace(text[0])))
        {
            return true;
        }

        startsTrace = false;
        return false;
    }
}
