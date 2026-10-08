namespace ShortsGenerator.Domain.Tests;

public class PlanVariationRulesTests
{
    [Fact]
    public void Check_VariedPlan_HasNoIssues()
    {
        var plan = Plan(
            S(1, SceneType.Hook, "mascot-thinking"),
            S(2, SceneType.ExerciseDemo, "pullup-bottom"),
            S(3, SceneType.MythVsFact, "mascot-idle"),
            S(4, SceneType.Progress, "dumbbell"),
            S(5, SceneType.List, "pullup-bar"),
            S(6, SceneType.Outro, "mascot-happy"));

        Assert.Empty(PlanVariationRules.Check(plan));
    }

    [Fact]
    public void Check_SameTypeThreeTimes_IsFlagged()
    {
        var plan = Plan(
            S(1, SceneType.Hook), S(2, SceneType.List), S(3, SceneType.Progress), S(4, SceneType.List),
            S(5, SceneType.MythVsFact), S(6, SceneType.List), S(7, SceneType.Outro));

        Assert.Contains(PlanVariationRules.Check(plan), r => r.Contains("List is used 3 times"));
    }

    [Fact]
    public void Check_ConsecutiveSameType_IsFlagged()
    {
        var plan = Plan(
            S(1, SceneType.Hook), S(2, SceneType.ExerciseDemo), S(3, SceneType.ExerciseDemo),
            S(4, SceneType.Progress), S(5, SceneType.List), S(6, SceneType.Outro));

        Assert.Contains(PlanVariationRules.Check(plan), r => r.Contains("Scenes 2 and 3 are both ExerciseDemo"));
    }

    [Fact]
    public void Check_TooFewDistinctTypes_IsFlagged()
    {
        var plan = Plan(
            S(1, SceneType.Hook), S(2, SceneType.List), S(3, SceneType.Progress), S(4, SceneType.List),
            S(5, SceneType.Progress), S(6, SceneType.Outro));

        Assert.Contains(PlanVariationRules.Check(plan), r => r.Contains("Only 2 different scene types"));
    }

    [Fact]
    public void Check_AssetShownThreeTimes_AndBackToBack_AreFlagged()
    {
        var plan = Plan(
            S(1, SceneType.Hook, "dumbbell"), S(2, SceneType.List, "dumbbell"), S(3, SceneType.Progress),
            S(4, SceneType.MythVsFact, "dumbbell"), S(5, SceneType.Recovery), S(6, SceneType.Outro));

        var issues = PlanVariationRules.Check(plan);

        Assert.Contains(issues, r => r.Contains("'dumbbell' is shown in 3 scenes"));
        Assert.Contains(issues, r => r.Contains("Scenes 1 and 2 both show 'dumbbell'"));
    }

    [Fact]
    public void Check_ScenesWithoutAssets_AreNotTreatedAsRepeats()
    {
        var plan = Plan(
            S(1, SceneType.Hook), S(2, SceneType.List), S(3, SceneType.Progress),
            S(4, SceneType.MythVsFact), S(5, SceneType.Outro));

        Assert.Empty(PlanVariationRules.Check(plan));
    }

    [Fact]
    public void CheckAgainstEarlier_SameOpeningAsAnEarlierVideo_IsFlagged()
    {
        var earlier = Plan(S(1, SceneType.Hook), S(2, SceneType.MythVsFact), S(3, SceneType.AnatomyHighlight),
            S(4, SceneType.Progress), S(5, SceneType.Outro));
        var plan = Plan(S(1, SceneType.Hook), S(2, SceneType.MythVsFact), S(3, SceneType.AnatomyHighlight),
            S(4, SceneType.Progress), S(5, SceneType.List), S(6, SceneType.Outro));

        var issues = PlanVariationRules.CheckAgainstEarlier(plan, [("Squat depth", earlier)]);

        Assert.Contains(issues, i => i.Contains("MythVsFact, AnatomyHighlight, Progress") && i.Contains("Squat depth"));
    }

    [Fact]
    public void CheckAgainstEarlier_DifferentOrder_IsFine()
    {
        var earlier = Plan(S(1, SceneType.Hook), S(2, SceneType.MythVsFact), S(3, SceneType.AnatomyHighlight),
            S(4, SceneType.Progress), S(5, SceneType.Outro));
        var plan = Plan(S(1, SceneType.Hook), S(2, SceneType.AnatomyHighlight), S(3, SceneType.MythVsFact),
            S(4, SceneType.Progress), S(5, SceneType.Outro));

        Assert.Empty(PlanVariationRules.CheckAgainstEarlier(plan, [("Squat depth", earlier)]));
    }

    private static VideoPlan Plan(params Scene[] scenes) => new("T", "H", 30, "n", scenes, null, null, null);

    private static Scene S(int order, SceneType type, string? asset = null) =>
        new($"s{order}", order, type, 4, "Narration.", null, asset is null ? [] : [asset], AnimationId.Fade, null, null, null);
}
