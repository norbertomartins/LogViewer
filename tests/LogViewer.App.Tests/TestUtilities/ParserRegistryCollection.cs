namespace LogViewer.App.Tests.TestUtilities;

/// <summary>
/// Tests that assert on the process-wide <c>LogLineParsers</c> custom-format registry. Every <c>MainViewModel</c>
/// constructed by a parallel test resets that registry from its own settings, so these run alone, after the
/// parallel tests (<see cref="CollectionDefinitionAttribute.DisableParallelization"/>).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ParserRegistryCollection
{
    public const string Name = "LogLineParsers registry";
}
