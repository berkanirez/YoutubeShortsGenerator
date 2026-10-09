using ShortsGenerator.Domain;

namespace ShortsGenerator.Application.Tests;

public class AssetMatcherTests
{
    private static readonly IReadOnlyList<AssetMetadata> Catalog =
    [
        A("biceps-highlight", ["biceps", "arms", "curl"], SceneType.AnatomyHighlight),
        A("core-highlight", ["core", "abs", "plank"], SceneType.AnatomyHighlight),
        A("quads-highlight", ["quads", "legs", "squat"], SceneType.AnatomyHighlight),
        A("squat-bottom", ["squat", "depth"], SceneType.ExerciseDemo, SceneType.Comparison),
        A("mascot-thinking", ["mascot", "thinking"], SceneType.Comparison),
        A("pullup-top", ["pull-up", "back"], SceneType.ExerciseDemo),
    ];

    [Fact]
    public void Improve_ChosenAssetUnrelatedToTheScene_IsSwappedForTheMatchingOne()
    {
        // The real Day 20 case: "Core Muscle" scene, model picked biceps-highlight.
        var result = AssetMatcher.Improve(["biceps-highlight"], SceneType.AnatomyHighlight,
            "Core Muscle A strong core keeps your spine stable.", Catalog);

        Assert.Equal(["core-highlight"], result);
    }

    [Fact]
    public void Improve_ChosenAssetThatFits_IsKept_EvenIfAnotherAlsoMatches()
    {
        var result = AssetMatcher.Improve(["quads-highlight"], SceneType.AnatomyHighlight,
            "Squats train your quads and your core.", Catalog);

        Assert.Equal(["quads-highlight"], result);
    }

    [Fact]
    public void Improve_OnlyConsidersAssetsCompatibleWithTheSceneType()
    {
        // squat-bottom is compatible with Comparison; quads-highlight (also tagged squat) is not.
        var result = AssetMatcher.Improve(["mascot-thinking"], SceneType.Comparison,
            "Keep your knees in line as you squat.", Catalog);

        Assert.Equal(["squat-bottom"], result);
    }

    [Fact]
    public void Improve_NoMatchAnywhere_KeepsTheModelsChoice()
    {
        var result = AssetMatcher.Improve(["squat-bottom"], SceneType.ExerciseDemo,
            "Breathe out on the way up.", Catalog);

        Assert.Equal(["squat-bottom"], result);
    }

    // --- Day 29: against the REAL catalog. The fake one above used the tag "pull-up" while
    // assets.json says "pullup" - these tests passed while real videos showed a squat drawing
    // in a pull-up video. Scene texts are from the five Day 29 videos.

    private static readonly Lazy<IReadOnlyList<AssetMetadata>> RealCatalog = new(LoadRealCatalog);

    [Fact]
    public void Real_AWrongMuscle_IsNeverKeptJustBecauseItIsTaggedMuscle()
    {
        // Push-up video: "Focus on chest muscles" was shown with biceps-highlight.
        var result = AssetMatcher.Improve(["biceps-highlight"], SceneType.AnatomyHighlight,
            "Focus on chest muscles Lower your body until your chest almost touches the ground", RealCatalog.Value,
            topic: "Can You Build Muscle With Push-Ups?");

        Assert.NotEqual(["biceps-highlight"], result);
        Assert.Contains("chest", RealCatalog.Value.Single(a => a.Id == result.Single()).Tags);
    }

    [Fact]
    public void Real_APoseWordLikeTop_DoesNotMakeASquatFitAPullUpScene_AndTheTopicDecides()
    {
        // Pull-up video, scene 3: squat-top kept because of "...squeezing your lats at the top".
        var result = AssetMatcher.Improve(["squat-top"], SceneType.ExerciseDemo,
            "Full Range Ensure you're starting with a full range of motion, squeezing your lats at the top, and fully lowering yourself down.",
            RealCatalog.Value, topic: "Why Your Pull-Ups Aren't Increasing");

        Assert.StartsWith("pullup-", result.Single());
    }

    [Fact]
    public void Real_AnExerciseDemoShowsTheTopicsExercise_UnlessTheSceneNamesAnotherOne()
    {
        // Day 29 round 2, push-up video: "...push back up" matched pull-up's "back" tag.
        var pushUp = AssetMatcher.Improve(["pullup-bottom"], SceneType.ExerciseDemo,
            "Lower yourself down slowly and push back up with control Lower yourself down slowly, avoiding jerky movements, and push back up with control.",
            RealCatalog.Value, topic: "Can You Build Muscle With Push-Ups?");
        // A scene that does name another exercise keeps it.
        var squat = AssetMatcher.Improve(["squat-bottom"], SceneType.ExerciseDemo,
            "Squats work your legs, push-ups your chest.", RealCatalog.Value, topic: "Can You Build Muscle With Push-Ups?");

        Assert.StartsWith("pushup-", pushUp.Single());
        Assert.Equal(["squat-bottom"], squat);
    }

    [Fact]
    public void Real_TheTopicsExercise_AlsoWinsWhenTheModelChoseNothing()
    {
        // Day 30's fresh run: no asset chosen, "...engage your core" found plank in a pull-up video.
        var result = AssetMatcher.Improve([], SceneType.ExerciseDemo,
            "Start with feet shoulder-width apart Make sure to start with your feet shoulder-width apart and engage your core.",
            RealCatalog.Value, topic: "Why Your Pull-Ups Aren't Increasing");

        Assert.StartsWith("pullup-", result.Single());
    }

    [Fact]
    public void Real_HyphenatedWordsInTheTextMatchJoinedTags()
    {
        Assert.StartsWith("pushup-", AssetMatcher.Improve([], SceneType.ExerciseDemo, "Do ten slow push-ups.", RealCatalog.Value).Single());
        Assert.StartsWith("pullup-", AssetMatcher.Improve([], SceneType.ExerciseDemo, "Strict pull-ups only.", RealCatalog.Value).Single());
    }

    [Fact]
    public void Real_AnAnatomySceneAboutNoMuscleInTheCatalog_ShowsNoMuscleRatherThanAWrongOne()
    {
        // Running video: AnatomyHighlight with biceps over "shin splints or plantar fasciitis".
        var result = AssetMatcher.Improve(["biceps-highlight"], SceneType.AnatomyHighlight,
            "High mileage or frequency can lead to overuse injuries like shin splints or plantar fasciitis.", RealCatalog.Value,
            topic: "Is Running Killing Your Gains?");

        Assert.Empty(result);   // the renderer's mascot fallback; a wrong muscle would teach something false
    }

    [Fact]
    public void Real_TheRightMuscle_IsKept()
    {
        Assert.Equal(["lats-highlight"], AssetMatcher.Improve(["lats-highlight"], SceneType.AnatomyHighlight,
            "Your lats do most of the pulling.", RealCatalog.Value));
    }

    // --- Day 29: repetition repaired in code instead of an extra LLM attempt (all 6 plan
    // rejections of the five videos were "same asset twice in a row" / "asset 3 times").

    private static Scene S(int order, SceneType type, string asset, string narration) =>
        new($"s{order}", order, type, 3, narration, null, asset.Length == 0 ? [] : [asset], AnimationId.Pop, null, null, null);

    [Fact]
    public void Spread_ConsecutiveRepeat_IsReplacedByAnotherAssetThatFitsTheScene()
    {
        // Soreness video: scenes 6 and 7 both showed 'meal'.
        IReadOnlyList<Scene> scenes =
        [
            S(1, SceneType.Hook, "", "Sore?"),
            S(2, SceneType.List, "meal", "Eat enough protein every day."),
            S(3, SceneType.Recovery, "meal", "Sleep and rest so your muscles recover."),
        ];

        var result = AssetMatcher.Spread(scenes, RealCatalog.Value, topic: "Soreness");

        Assert.Equal(["meal"], result[1].AssetIds);
        Assert.Equal(["mascot-sleeping"], result[2].AssetIds);   // tagged sleep/rest/recovery, like the 'sleep' icon
    }

    [Fact]
    public void Spread_AThirdUse_IsReplaced_OrLeftToTheMascotWhenNothingElseFits()
    {
        // Rest-days video: 'sleep' in 3 scenes (2 and 3 in a row).
        IReadOnlyList<Scene> scenes =
        [
            S(1, SceneType.Recovery, "sleep", "Rest days matter."),
            S(2, SceneType.List, "calendar", "Plan your week."),
            S(3, SceneType.Recovery, "sleep", "Rest helps you recover."),
            S(4, SceneType.Recovery, "sleep", "Rest again."),
        ];

        var result = AssetMatcher.Spread(scenes, RealCatalog.Value, topic: "Why Rest Days Make You Stronger");

        Assert.True(result.Count(s => s.AssetIds.Contains("sleep")) <= PlanVariationRules.MaxUsesPerAsset);
        for (var i = 1; i < result.Count; i++)
        {
            var previous = result[i - 1].AssetIds.FirstOrDefault();
            Assert.True(previous is null || previous != result[i].AssetIds.FirstOrDefault(), $"scenes {i} and {i + 1} repeat");
        }

        var plan = new VideoPlan("T", "H", 12, "n", result, null, null, null);
        Assert.DoesNotContain(PlanVariationRules.Check(plan), r => r.Contains("sleep"));
    }

    [Fact]
    public void Spread_NoRepetition_ChangesNothing()
    {
        IReadOnlyList<Scene> scenes = [S(1, SceneType.Recovery, "sleep", "Rest."), S(2, SceneType.List, "meal", "Eat.")];

        Assert.Equal(scenes, AssetMatcher.Spread(scenes, RealCatalog.Value, topic: ""));
    }

    private static IReadOnlyList<AssetMetadata> LoadRealCatalog()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ShortsGenerator.slnx")))
        {
            directory = directory.Parent;
        }

        var json = File.ReadAllText(Path.Combine(directory!.FullName, "assets", "manifests", "assets.json"));
        return System.Text.Json.JsonSerializer.Deserialize<List<AssetMetadata>>(json, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)
        {
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        })!;
    }

    [Fact]
    public void Improve_EmptyChoice_GetsAMatchOnlyWhenOneExists()
    {
        Assert.Equal(["pullup-top"], AssetMatcher.Improve([], SceneType.ExerciseDemo, "Do strict pull-ups.", Catalog));
        Assert.Empty(AssetMatcher.Improve([], SceneType.ExerciseDemo, "Breathe out.", Catalog));
    }

    [Fact]
    public void Improve_SimplePluralsAndHyphenatedTagsMatch()
    {
        Assert.Equal(["pullup-top"], AssetMatcher.Improve(["squat-bottom"], SceneType.ExerciseDemo, "More pull-ups, fewer excuses.", Catalog));
    }

    private static AssetMetadata A(string id, string[] tags, params SceneType[] types) =>
        new(id, $"../x/{id}.svg", "anatomy", tags, types, AssetOrientation.Square, null);
}
