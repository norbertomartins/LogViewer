using LogViewer.Core.ExternalTools;

namespace LogViewer.Core.Tests.ExternalTools;

public sealed class ExternalToolLauncherTests
{
    [Fact]
    public void BuildArguments_SubstitutesAllPlaceholders()
    {
        var context = new ExternalToolContext(@"C:\logs\app.log", 42, "ERROR something failed");

        var result = ExternalToolLauncher.BuildArguments("open \"{FilePath}\" --line {LineNumber} --text \"{LineText}\"", context);

        Assert.Equal(@"open ""C:\logs\app.log"" --line 42 --text ""ERROR something failed""", result);
    }

    [Fact]
    public void BuildArguments_MissingLineContext_SubstitutesEmptyString()
    {
        var context = new ExternalToolContext(@"C:\logs\app.log", LineNumber: null, LineText: null);

        var result = ExternalToolLauncher.BuildArguments("{FilePath} [{LineNumber}] {LineText}", context);

        Assert.Equal(@"C:\logs\app.log [] ", result);
    }

    [Fact]
    public void BuildArguments_NoPlaceholders_ReturnsTemplateUnchanged()
    {
        var context = new ExternalToolContext(@"C:\logs\app.log", 1, "text");

        var result = ExternalToolLauncher.BuildArguments("--fixed-arg --another", context);

        Assert.Equal("--fixed-arg --another", result);
    }

    [Fact]
    public void BuildArguments_RepeatedPlaceholder_SubstitutesEveryOccurrence()
    {
        var context = new ExternalToolContext(@"C:\logs\app.log", 7, "text");

        var result = ExternalToolLauncher.BuildArguments("{FilePath} {FilePath}", context);

        Assert.Equal(@"C:\logs\app.log C:\logs\app.log", result);
    }

    [Fact]
    public void TryLaunch_NonExistentExecutable_ReturnsFalseWithError()
    {
        var tool = new ExternalToolDefinition(
            Guid.NewGuid(), "Bad Tool", @"C:\definitely\not\a\real\tool.exe", "{FilePath}", null, false, null);
        var context = new ExternalToolContext(@"C:\logs\app.log", null, null);

        var launched = ExternalToolLauncher.TryLaunch(tool, context, out var error);

        Assert.False(launched);
        Assert.NotNull(error);
        Assert.Contains("Bad Tool", error);
    }
}
