using LogViewer.Core.Highlighting;
using LogViewer.Core.Structured;

namespace LogViewer.Core.Tests.Highlighting;

public sealed class HighlightEngineTests
{
    [Fact]
    public void Evaluate_KeywordMatch_ReturnsMatch()
    {
        var engine = new HighlightEngine();
        var rule = HighlightRule.CreateDefault("Errors", "ERROR");
        engine.SetRules([rule]);

        var match = engine.Evaluate("2026-07-31 12:00:00 ERROR something broke");

        Assert.NotNull(match);
        Assert.Equal(rule.Id, match!.RuleId);
    }

    [Fact]
    public void Evaluate_KeywordNoMatch_ReturnsNull()
    {
        var engine = new HighlightEngine();
        engine.SetRules([HighlightRule.CreateDefault("Errors", "ERROR")]);

        var match = engine.Evaluate("all good here");

        Assert.Null(match);
    }

    [Fact]
    public void Evaluate_RegexMatch_ReturnsMatch()
    {
        var engine = new HighlightEngine();
        var rule = HighlightRule.CreateDefault("Warnings", @"\bWARN(ING)?\b", isRegex: true);
        engine.SetRules([rule]);

        Assert.NotNull(engine.Evaluate("this is a WARNING message"));
        Assert.NotNull(engine.Evaluate("WARN: low disk space"));
        Assert.Null(engine.Evaluate("no issue here"));
    }

    [Fact]
    public void Evaluate_CaseInsensitiveByDefault()
    {
        var engine = new HighlightEngine();
        engine.SetRules([HighlightRule.CreateDefault("Errors", "error")]);

        Assert.NotNull(engine.Evaluate("An ERROR occurred"));
    }

    [Fact]
    public void Evaluate_CaseSensitive_RespectsCasing()
    {
        var engine = new HighlightEngine();
        var rule = HighlightRule.CreateDefault("Errors", "ERROR") with { IsCaseSensitive = true };
        engine.SetRules([rule]);

        Assert.NotNull(engine.Evaluate("ERROR: failed"));
        Assert.Null(engine.Evaluate("error: failed"));
    }

    [Fact]
    public void Evaluate_FirstRuleInListOrderWinsOnOverlap()
    {
        var engine = new HighlightEngine();
        var first = HighlightRule.CreateDefault("First", "fail");
        var second = HighlightRule.CreateDefault("Second", "fail");
        engine.SetRules([first, second]);

        var match = engine.Evaluate("operation failed");

        Assert.Equal(first.Id, match!.RuleId);
    }

    [Fact]
    public void Evaluate_DisabledRule_NeverMatches()
    {
        var engine = new HighlightEngine();
        engine.SetRules([HighlightRule.CreateDefault("Errors", "ERROR") with { IsEnabled = false }]);

        Assert.Null(engine.Evaluate("ERROR here"));
    }

    [Fact]
    public void Evaluate_TargetProperty_MatchesOnlyThatPropertyValue()
    {
        var engine = new HighlightEngine();
        var rule = HighlightRule.CreateDefault("Error level", "Error") with { TargetProperty = StructuredFieldResolver.LevelField };
        engine.SetRules([rule]);

        var line = @"{""@t"":""2026-01-01T00:00:00Z"",""@mt"":""Error occurred"",""@l"":""Error""}";
        SerilogEventParser.TryParse(line, out var structured);

        var match = engine.Evaluate(line, structured);

        Assert.NotNull(match);
        Assert.Equal(rule.Id, match!.RuleId);
    }

    [Fact]
    public void Evaluate_TargetProperty_DoesNotFallBackToWholeLine()
    {
        var engine = new HighlightEngine();
        var rule = HighlightRule.CreateDefault("Error level", "Error") with { TargetProperty = StructuredFieldResolver.LevelField };
        engine.SetRules([rule]);

        // The raw text mentions "Error" but the @l field is "Warning" — the rule must not match on raw text.
        var line = @"{""@t"":""2026-01-01T00:00:00Z"",""@mt"":""Error-adjacent warning"",""@l"":""Warning""}";
        SerilogEventParser.TryParse(line, out var structured);

        Assert.Null(engine.Evaluate(line, structured));
    }

    [Fact]
    public void Evaluate_TargetProperty_NoStructuredEvent_NeverMatches()
    {
        var engine = new HighlightEngine();
        var rule = HighlightRule.CreateDefault("Error level", "Error") with { TargetProperty = StructuredFieldResolver.LevelField };
        engine.SetRules([rule]);

        Assert.Null(engine.Evaluate("plain text line with Error in it", structured: null));
    }
}
