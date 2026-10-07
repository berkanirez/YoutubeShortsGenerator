namespace ShortsGenerator.Application.Tests;

public class ScenePlanPromptBuilderTests
{
    [Fact]
    public void Build_IncludesTopicAndNarrationVerbatim()
    {
        var prompt = ScenePlanPromptBuilder.Build("Running basics", "Run slow to run fast later.");

        Assert.Contains("Running basics", prompt);
        Assert.Contains("Run slow to run fast later.", prompt);
    }

    [Fact]
    public void Build_ListsAllowedSceneTypesAndAnimations()
    {
        var prompt = ScenePlanPromptBuilder.Build("Running basics", "Run slow to run fast later.");

        Assert.Contains("MythVsFact", prompt);
        Assert.Contains("punchZoom", prompt);
        Assert.Contains("whoosh", prompt);
    }

    [Fact]
    public void Build_WithoutFailureReasons_DoesNotMentionPreviousAttempt()
    {
        var prompt = ScenePlanPromptBuilder.Build("Running basics", "Run slow to run fast later.");

        Assert.DoesNotContain("previous attempt", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_WithFailureReasons_IncludesThemForRepair()
    {
        var prompt = ScenePlanPromptBuilder.Build(
            "Running basics", "Run slow to run fast later.", ["The first scene must be of type 'Hook'."]);

        Assert.Contains("previous attempt", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("The first scene must be of type 'Hook'.", prompt);
    }
}
