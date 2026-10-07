namespace ShortsGenerator.Application.Tests;

public class ScriptPromptBuilderTests
{
    [Fact]
    public void Build_IncludesTheTopic()
    {
        var prompt = ScriptPromptBuilder.Build("Why Your Pull-Ups Aren't Increasing");

        Assert.Contains("Why Your Pull-Ups Aren't Increasing", prompt);
    }

    [Fact]
    public void Build_IncludesSafetyRules()
    {
        var prompt = ScriptPromptBuilder.Build("Running basics");

        Assert.Contains("diagnose", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cure", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_WithoutFailureReasons_DoesNotMentionPreviousAttempt()
    {
        var prompt = ScriptPromptBuilder.Build("Running basics");

        Assert.DoesNotContain("previous attempt", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_WithFailureReasons_IncludesThemForRepair()
    {
        var prompt = ScriptPromptBuilder.Build("Running basics", ["Too short (10 words; needs at least 40)."]);

        Assert.Contains("previous attempt", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Too short (10 words; needs at least 40).", prompt);
    }
}
