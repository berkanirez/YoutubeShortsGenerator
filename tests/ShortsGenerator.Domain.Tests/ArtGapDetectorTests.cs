namespace ShortsGenerator.Domain.Tests;

public class ArtGapDetectorTests
{
    private static readonly ArtSubject Biceps = new("biceps", ArtKind.Muscle, ["biceps", "bicep", "arms"], "BICEPS on both upper arms");
    private static readonly ArtSubject Forearms = new("forearms", ArtKind.Muscle, ["forearms", "forearm", "grip"], "FOREARMS");
    private static readonly ArtSubject Lats = new("lats", ArtKind.Muscle, ["lats", "back"], "LATS", BackView: true);
    private static readonly ArtSubject PullUp = new("pullup", ArtKind.ExercisePair, ["pull ups", "pull up"], "pull-up",
        Top: "chin above the bar", Bottom: "arms straight", Camera: "front camera, same bar height");
    private static readonly ArtSubject Curl = new("curl", ArtKind.ExercisePair, ["curls", "curl", "biceps curl"], "curl",
        Top: "curled up", Bottom: "arm straight");
    private static readonly ArtSubject PullUpBar = new("pullup-bar", ArtKind.Equipment, ["pull up bar"], "a doorway pull-up bar");
    private static readonly ArtSubject Water = new("water", ArtKind.Icon, ["water", "hydration"], "a clear water bottle");

    private static readonly ArtSubject[] Subjects = [Biceps, Forearms, Lats, PullUp, Curl, PullUpBar, Water];

    private static Scene S(string id, int order, SceneType type, string narration, string? onScreen = null, params string[] assets) =>
        new(id, order, type, 4, narration, onScreen, assets, AnimationId.Pop, null, null, null);

    [Fact]
    public void AForearmScene_AsksForForearms_InsteadOfShowingTheBiceps()
    {
        var scene = S("s2", 2, SceneType.AnatomyHighlight, "Your forearms decide how long you can hang.", "Forearms", "biceps-highlight");

        var check = ArtGapDetector.Check([scene], ["biceps-highlight"], Subjects);

        var request = Assert.Single(check.Requests);
        Assert.Equal("forearms", request.Subject.Id);
        Assert.Equal("forearms-highlight.png", request.Subject.IncomingFileName);
        Assert.Equal(["s2"], request.SceneIds);
    }

    [Fact]
    public void ASubjectTheCatalogHas_IsShown_EvenWhenThePlanPickedSomethingElse()
    {
        var scene = S("s3", 3, SceneType.AnatomyHighlight, "Your lats do most of the pulling.", "Lats", "biceps-highlight");

        var check = ArtGapDetector.Check([scene], ["biceps-highlight", "lats-highlight"], Subjects);

        Assert.Empty(check.Requests);
        Assert.Equal(["lats-highlight"], check.Scenes[0].AssetIds);
    }

    [Fact]
    public void ThePlansOwnPoseOfTheRightExercise_IsKept()
    {
        var scene = S("s4", 4, SceneType.ExerciseDemo, "Start every Pull-Up from a dead hang.", null, "pullup-bottom");

        var check = ArtGapDetector.Check([scene], ["pullup-top", "pullup-bottom"], Subjects);

        Assert.Equal(["pullup-bottom"], check.Scenes[0].AssetIds);
    }

    [Fact]
    public void AnExerciseScene_LooksForExercises_NotMuscles()
    {
        // "biceps curl": the muscle comes first in the sentence, but a demo shows the movement.
        var scene = S("s2", 2, SceneType.ExerciseDemo, "Do your biceps curls slowly.");

        var check = ArtGapDetector.Check([scene], [], Subjects);

        Assert.Equal("curl", Assert.Single(check.Requests).Subject.Id);
    }

    [Fact]
    public void TheLongerTermWins_APullUpBarIsNotAPullUp()
    {
        var scene = S("s5", 5, SceneType.List, "A pull-up bar at home removes every excuse.");

        Assert.Equal("pullup-bar", ArtGapDetector.FindSubject(scene, Subjects)!.Id);
    }

    [Fact]
    public void TheOnScreenSlogan_DecidesBeforeTheNarration()
    {
        var scene = S("s3", 3, SceneType.AnatomyHighlight, "Strong arms start with a strong grip.", "Grip");

        Assert.Equal("forearms", ArtGapDetector.FindSubject(scene, Subjects)!.Id);
    }

    [Fact]
    public void HookAndOutro_StayOnTheMascot()
    {
        var scenes = new[]
        {
            S("s1", 1, SceneType.Hook, "Are your forearms holding back your pull-ups?"),
            S("s6", 6, SceneType.Outro, "Drink water and train your grip."),
        };

        Assert.Empty(ArtGapDetector.Check(scenes, [], Subjects).Requests);
    }

    [Fact]
    public void TwoScenesNeedingTheSamePicture_AskForItOnce()
    {
        var scenes = new[]
        {
            S("s2", 2, SceneType.List, "Drink water before you train."),
            S("s4", 4, SceneType.Recovery, "Hydration speeds up recovery."),
        };

        var request = Assert.Single(ArtGapDetector.Check(scenes, [], Subjects).Requests);
        Assert.Equal(["s2", "s4"], request.SceneIds);
    }

    [Fact]
    public void WordsInsideOtherWords_DontCount()
    {
        // "arms" inside "forearms" is not the biceps.
        var scene = S("s2", 2, SceneType.AnatomyHighlight, "Train your forearms.");

        Assert.Equal("forearms", ArtGapDetector.FindSubject(scene, [Biceps, Forearms])!.Id);
    }

    [Fact]
    public void Prompts_AreBuiltFromTheSubject_WithTheCharacterAndTheRightLayout()
    {
        var pair = ArtPromptBuilder.Build(PullUp);
        Assert.StartsWith(ArtPromptBuilder.Character, pair);
        Assert.Contains("LEFT half: chin above the bar.", pair);
        Assert.Contains("RIGHT half: arms straight.", pair);

        var muscle = ArtPromptBuilder.Build(Lats);
        Assert.Contains("BACK view", muscle);
        Assert.Contains("The LATS are colored solid bright red", muscle);

        var icon = ArtPromptBuilder.Build(Water);
        Assert.StartsWith("A clear water bottle, as a simple icon", icon);
        Assert.False(ArtPromptBuilder.NeedsReferenceImage(Water));
    }

    [Fact]
    public void AssetIdsAndFileNames_FollowTheCatalogConventions()
    {
        Assert.Equal(["pullup-top", "pullup-bottom"], PullUp.AssetIds);
        Assert.Equal("pullup-pair.png", PullUp.IncomingFileName);
        Assert.Equal(["forearms-highlight"], Forearms.AssetIds);
        Assert.Equal("anatomy", Forearms.Category);
        Assert.Equal("water.png", Water.IncomingFileName);
        Assert.Equal("icon", Water.Category);
        Assert.Equal("pull-up-bar", PullUpBar.Tags[1]);
    }
}

public class ArtGapDetectorRepetitionTests
{
    private static readonly ArtSubject Squat = new("squat", ArtKind.ExercisePair, ["squats", "squat"], "squat", Top: "up", Bottom: "down");

    private static Scene S(int order, SceneType type, string narration, params string[] assets) =>
        new($"s{order}", order, type, 4, narration, null, assets, AnimationId.Pop, null, null, null);

    [Fact]
    public void ASubjectsPicture_ShowsInAtMostTwoScenes_TheDemoFirst()
    {
        IReadOnlyList<Scene> scenes =
        [
            S(2, SceneType.Comparison, "Half squats rob you.", "squat-bottom"),
            S(3, SceneType.List, "Squat twice a week."),
            S(4, SceneType.ExerciseDemo, "Sit back into the squat."),
            S(5, SceneType.Progress, "Add five pounds to your squat.", "squat-top"),
        ];

        var check = ArtGapDetector.Check(scenes, ["squat-top", "squat-bottom"], [Squat]);

        Assert.Equal(["s2", "s3", "s4", "s5"], check.Scenes.Select(s => s.Id));          // order kept
        Assert.Equal(["squat-top"], check.Scenes[2].AssetIds);                            // the demo got it first
        Assert.Equal(2, check.Scenes.Count(s => s.AssetIds.Any(a => a.StartsWith("squat-"))));
        Assert.Empty(check.Scenes[3].AssetIds);                                           // over the limit: back to the mascot
    }
}
