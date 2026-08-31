namespace LogViewer.Core.Structured;

/// <summary>Adapts the existing stateless <see cref="SerilogEventParser"/> to the <see cref="ILogLineParser"/>
/// abstraction so Serilog/CLEF sits alongside the other formats in <see cref="LogLineParsers"/>.</summary>
public sealed class SerilogLogLineParser : ILogLineParser
{
    public string FormatId => "serilog";

    public string DisplayName => "Serilog / CLEF (JSON)";

    public bool TryParse(string line, out StructuredLogEvent? evt) => SerilogEventParser.TryParse(line, out evt);
}
