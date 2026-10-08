using ShortsGenerator.Domain;

namespace ShortsGenerator.Application.Tests;

public class ScenePlanPromptBuilderTests
{
    private static readonly IReadOnlyList<AssetMetadata> SampleAssets =
    [
        new AssetMetadata(
            "mascot-idle", "assets/mascot/mascot-idle.svg", "mascot", ["mascot", "idle"],
            [SceneType.Hook], AssetOrientation.Square, "center"),
    ];

    private static readonly IReadOnlyList<string> Scenes = ["Run slow.", "Then run fast later.", "Rest well."];

    [Fact]
    public void Build_NumbersEveryPreSplitSceneAndAsksForExactlyThatMany()
    {
        var prompt = ScenePlanPromptBuilder.Build("Running basics", Scenes, SampleAssets);

        Assert.Contains("Running basics", prompt);
        Assert.Contains("1. \"Run slow.\"", prompt);
        Assert.Contains("3. \"Rest well.\"", prompt);
        Assert.Contains("exactly 3 entries", prompt);
    }

    [Fact]
    public void Build_DoesNotAskTheModelToWriteNarrationOrDurations()
    {
        var prompt = ScenePlanPromptBuilder.Build("Running basics", Scenes, SampleAssets);

        Assert.DoesNotContain("\"narration\":", prompt);
        Assert.DoesNotContain("durationSeconds", prompt);
    }

    [Fact]
    public void Build_ListsAllowedMiddleSceneTypesAnimationsAndSfx()
    {
        var prompt = ScenePlanPromptBuilder.Build("Running basics", Scenes, SampleAssets);

        Assert.Contains("MythVsFact", prompt);
        Assert.Contains("punchZoom", prompt);
        Assert.Contains("whoosh", prompt);
    }

    [Fact]
    public void Build_ListsKnownAssetCatalogWithTags()
    {
        var prompt = ScenePlanPromptBuilder.Build("Running basics", Scenes, SampleAssets);

        Assert.Contains("mascot-idle (mascot: mascot, idle)", prompt);
    }

    [Fact]
    public void Build_VariationSeed_ReordersTheTemplateGuide_Deterministically()
    {
        var a = ScenePlanPromptBuilder.Build("Running basics", Scenes, SampleAssets, variationSeed: 0);
        var b = ScenePlanPromptBuilder.Build("Running basics", Scenes, SampleAssets, variationSeed: 3);

        Assert.Equal(a, ScenePlanPromptBuilder.Build("Running basics", Scenes, SampleAssets, variationSeed: 0));
        Assert.NotEqual(a.IndexOf("* MythVsFact", StringComparison.Ordinal) < a.IndexOf("* Progress", StringComparison.Ordinal),
                        b.IndexOf("* MythVsFact", StringComparison.Ordinal) < b.IndexOf("* Progress", StringComparison.Ordinal));
        Assert.Contains("* AnatomyHighlight", b);
    }

    [Fact]
    public void Build_WithoutFailureReasons_DoesNotMentionPreviousAttempt()
    {
        var prompt = ScenePlanPromptBuilder.Build("Running basics", Scenes, SampleAssets);

        Assert.DoesNotContain("previous attempt", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_WithFailureReasons_IncludesThemForRepair()
    {
        var prompt = ScenePlanPromptBuilder.Build(
            "Running basics", Scenes, SampleAssets, ["You returned 2 scenes; return exactly 3."]);

        Assert.Contains("previous attempt", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("You returned 2 scenes; return exactly 3.", prompt);
    }
}
