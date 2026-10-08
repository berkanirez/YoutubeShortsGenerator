namespace ShortsGenerator.Domain.Tests;

public class VideoPlanValidatorTests
{
    private const string Narration =
        "Pull ups are a great back exercise. Keep your core tight throughout the movement. " +
        "Pull your chest towards the bar, not just your chin. Lower yourself under control " +
        "every single rep. Add weight once you can do ten clean reps. Track your progress " +
        "every single week.";

    private static readonly HashSet<string> NoAssets = [];

    [Fact]
    public void Validate_WellFormedSixScenePlan_IsValid()
    {
        var plan = BuildValidPlan();

        var result = VideoPlanValidator.Validate(plan, Narration, NoAssets);

        Assert.True(result.IsValid);
        Assert.Empty(result.Reasons);
    }

    [Fact]
    public void Validate_TooFewScenes_IsInvalid()
    {
        var plan = BuildValidPlan(sceneCount: 3);

        var result = VideoPlanValidator.Validate(plan, Narration, NoAssets);

        Assert.False(result.IsValid);
        Assert.Contains(result.Reasons, r => r.Contains("must have between", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_FirstSceneNotHook_IsInvalid()
    {
        var scenes = BuildScenes(6).ToList();
        scenes[0] = scenes[0] with { Type = SceneType.ExerciseDemo };
        var plan = BuildValidPlan(scenes: scenes);

        var result = VideoPlanValidator.Validate(plan, Narration, NoAssets);

        Assert.False(result.IsValid);
        Assert.Contains(result.Reasons, r => r.Contains("must be of type 'Hook'", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_LastSceneNotOutro_IsInvalid()
    {
        var scenes = BuildScenes(6).ToList();
        scenes[^1] = scenes[^1] with { Type = SceneType.Recovery };
        var plan = BuildValidPlan(scenes: scenes);

        var result = VideoPlanValidator.Validate(plan, Narration, NoAssets);

        Assert.False(result.IsValid);
        Assert.Contains(result.Reasons, r => r.Contains("must be of type 'Outro'", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_DuplicateOrderValues_IsInvalid()
    {
        var scenes = BuildScenes(6).ToList();
        scenes[1] = scenes[1] with { Order = scenes[0].Order };
        var plan = BuildValidPlan(scenes: scenes);

        var result = VideoPlanValidator.Validate(plan, Narration, NoAssets);

        Assert.False(result.IsValid);
        Assert.Contains(result.Reasons, r => r.Contains("order", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_SceneDurationOutOfRange_IsInvalid()
    {
        var scenes = BuildScenes(6).ToList();
        scenes[2] = scenes[2] with { DurationSeconds = 99 };
        var plan = BuildValidPlan(scenes: scenes);

        var result = VideoPlanValidator.Validate(plan, Narration, NoAssets);

        Assert.False(result.IsValid);
        Assert.Contains(result.Reasons, r => r.Contains("duration", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_InvalidAssetId_IsInvalid()
    {
        var scenes = BuildScenes(6).ToList();
        scenes[0] = scenes[0] with { AssetIds = ["../etc/passwd"] };
        var plan = BuildValidPlan(scenes: scenes);

        var result = VideoPlanValidator.Validate(plan, Narration, NoAssets);

        Assert.False(result.IsValid);
        Assert.Contains(result.Reasons, r => r.Contains("invalid asset id", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_WellFormedButUnknownAssetId_IsInvalid()
    {
        var scenes = BuildScenes(6).ToList();
        scenes[0] = scenes[0] with { AssetIds = ["mascot-idle"] };
        var plan = BuildValidPlan(scenes: scenes);

        var result = VideoPlanValidator.Validate(plan, Narration, NoAssets);

        Assert.False(result.IsValid);
        Assert.Contains(result.Reasons, r => r.Contains("unknown asset id", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_AssetIdPresentInCatalog_IsValid()
    {
        var scenes = BuildScenes(6).ToList();
        scenes[0] = scenes[0] with { AssetIds = ["mascot-idle"] };
        var plan = BuildValidPlan(scenes: scenes);

        var result = VideoPlanValidator.Validate(plan, Narration, new HashSet<string> { "mascot-idle" });

        Assert.True(result.IsValid);
        Assert.Empty(result.Reasons);
    }

    [Fact]
    public void Validate_NarrationDoesNotReconstruct_IsInvalid()
    {
        var scenes = BuildScenes(6).ToList();
        scenes[0] = scenes[0] with { Narration = "This paraphrases the opening instead of copying it." };
        var plan = BuildValidPlan(scenes: scenes);

        var result = VideoPlanValidator.Validate(plan, Narration, NoAssets);

        Assert.False(result.IsValid);
        Assert.Contains(result.Reasons, r => r.Contains("does not exactly reproduce", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_NonOutroSceneWithEmptyNarration_IsInvalid()
    {
        var scenes = BuildScenes(6).ToList();
        scenes[2] = scenes[2] with { Narration = "   " };
        var plan = BuildValidPlan(scenes: scenes);

        var result = VideoPlanValidator.Validate(plan, Narration, NoAssets);

        Assert.False(result.IsValid);
        Assert.Contains(result.Reasons, r => r.Contains("empty narration", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_OutroSceneWithNullNarration_IsValid()
    {
        // A silent, visual-only closing card (CLAUDE.md's OutroCard) is legitimate when the
        // narration has no natural closing line left over for it.
        var scenes = BuildScenes(6).ToList();
        var outro = scenes[^1];
        scenes[^1] = outro with { Narration = null };
        var plan = BuildValidPlan(scenes: scenes) with
        {
            // The removed Outro narration is no longer part of the reconstructed text.
            NarrationText = string.Join(' ', scenes.Take(scenes.Count - 1).Select(s => s.Narration)),
        };

        var result = VideoPlanValidator.Validate(plan, plan.NarrationText, NoAssets);

        Assert.True(result.IsValid);
        Assert.Empty(result.Reasons);
    }

    [Fact]
    public void Validate_TotalSceneDurationOutsideAllowedVideoLength_IsInvalid()
    {
        // Each scene duration (12s) is individually within MaxSceneDurationSeconds, but six
        // of them push the whole video past MaxTargetDurationSeconds (50s).
        var scenes = BuildScenes(6).Select(s => s with { DurationSeconds = 12 }).ToList();
        var plan = BuildValidPlan(scenes: scenes) with { TargetDurationSeconds = 72 };

        var result = VideoPlanValidator.Validate(plan, Narration, NoAssets);

        Assert.False(result.IsValid);
        Assert.Contains(result.Reasons, r => r.Contains("whole video must be between", StringComparison.OrdinalIgnoreCase));
    }

    private static VideoPlan BuildValidPlan(int sceneCount = 6, List<Scene>? scenes = null)
    {
        var finalScenes = scenes ?? BuildScenes(sceneCount).ToList();
        var totalDuration = finalScenes.Sum(s => s.DurationSeconds);

        return new VideoPlan(
            Title: "Why Your Pull-Ups Aren't Increasing",
            Hook: "Stuck at the same pull-up count for months?",
            TargetDurationSeconds: totalDuration,
            NarrationText: Narration,
            Scenes: finalScenes,
            Style: null,
            CaptionStyle: null,
            MusicMood: null);
    }

    private static IEnumerable<Scene> BuildScenes(int count)
    {
        var sentences = Narration.Split(". ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select((s, i) => i == count - 1 ? s.TrimEnd('.') + "." : s + ".")
            .ToList();

        // For tests that ask for fewer/more scenes than there are sentences, just repeat
        // the last sentence - these plans are invalid for other reasons (scene count) and
        // narration-reconstruction isn't what's under test there.
        while (sentences.Count < count)
        {
            sentences.Add(sentences[^1]);
        }

        for (var i = 0; i < count; i++)
        {
            var type = i == 0 ? SceneType.Hook : i == count - 1 ? SceneType.Outro : SceneType.ExerciseDemo;
            yield return new Scene(
                Id: $"scene-{i + 1}",
                Order: i + 1,
                Type: type,
                DurationSeconds: 5,
                Narration: sentences[i],
                OnScreenText: null,
                AssetIds: [],
                Animation: AnimationId.Fade,
                Sfx: null,
                Background: null,
                Emphasis: null);
        }
    }
}
