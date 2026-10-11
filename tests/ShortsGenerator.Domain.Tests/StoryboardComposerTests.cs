namespace ShortsGenerator.Domain.Tests;

public class StoryboardComposerTests
{
    private static readonly Dictionary<string, ArtSubject> Subjects = new[]
    {
        new ArtSubject("pushup", ArtKind.ExercisePair, ["push up"], "push-up", Top: "up", Bottom: "down"),
        new ArtSubject("plank", ArtKind.Exercise, ["plank"], "plank"),
        new ArtSubject("pushup-hips-sag", ArtKind.Mistake, ["sagging hips"], "hips sag", Exercise: "pushup", Pose: "top",
            Label: "SAGGING HIPS", FixLabel: "SQUEEZE GLUTES", Point: [0.55, 0.42], Arrow: "up"),
        new ArtSubject("pushup-elbows-flare", ArtKind.Mistake, ["flared elbows"], "elbows out", Exercise: "pushup", Pose: "bottom",
            Label: "FLARED ELBOWS", FixLabel: "ELBOWS IN", Point: [0.2, 0.68], Arrow: "in"),
        new ArtSubject("plank-hips-high", ArtKind.Mistake, ["hips too high"], "hips up", Exercise: "plank", Pose: "single",
            Label: "HIPS TOO HIGH", FixLabel: "STRAIGHT LINE", Point: [0.55, 0.35], Arrow: "down"),
        new ArtSubject("dead-hang", ArtKind.Exercise, ["dead hang"], "hang", Hanging: true),
        new ArtSubject("chin-up", ArtKind.ExercisePair, ["chin up"], "chin-up", Top: "up", Bottom: "down", Hanging: true),
        new ArtSubject("pullup", ArtKind.ExercisePair, ["pull up"], "pull-up", Top: "up", Bottom: "down", Hanging: true),
        new ArtSubject("muscle-up", ArtKind.ExercisePair, ["muscle up"], "muscle-up", Top: "up", Bottom: "down", Hanging: true),
    }.ToDictionary(s => s.Id);

    private static readonly string[] Catalog =
        ["pushup-top", "pushup-bottom", "plank", "dead-hang", "chin-up-top", "chin-up-bottom", "pullup-top", "pullup-bottom", "muscle-up-top", "muscle-up-bottom"];

    private static Storyboard Countdown() => new(
        StoryboardFormat.Countdown,
        "Your push-ups are wrecking your back",
        "Here are three mistakes, and number one surprises everyone",
        [
            new StoryboardItem("plank-hips-high", "your hips point at the ceiling", "lower them into one straight line"),
            new StoryboardItem("pushup-elbows-flare", "your elbows flare straight out", "tuck them in toward your ribs"),
            new StoryboardItem("pushup-hips-sag", "your hips sag toward the floor", "squeeze your glutes the whole set"),
        ],
        "Fix these and every rep counts",
        "Clean reps",
        "Which one were you doing?");

    [Fact]
    public void Countdown_BecomesTenBeats_EachWithAShot_AndTheScriptIsTheirNarration()
    {
        var composed = StoryboardComposer.Compose(Countdown(), Catalog, Subjects);

        Assert.Equal(10, composed.Scenes.Count);
        Assert.All(composed.Scenes, s => Assert.NotNull(s.Shot));   // the mute test: every beat is a visual
        Assert.Equal(SceneType.Hook, composed.Scenes[0].Type);
        Assert.Equal(SceneType.Outro, composed.Scenes[^1].Type);
        Assert.Equal(string.Join(' ', composed.Scenes.Select(s => s.Narration)), composed.Narration);
        Assert.StartsWith("Number one: Your hips point at the ceiling.", composed.Scenes[2].Narration);
        Assert.StartsWith("Number three:", composed.Scenes[6].Narration);   // the most important last
    }

    [Fact]
    public void TheStressedPhrase_IsTheLabelAsTheSentenceSaysIt_EvenInOtherWordForms()
    {
        var composed = StoryboardComposer.Compose(Countdown(), [.. Catalog, "pushup-elbows-flare", "pushup-hips-sag"], Subjects);

        Assert.Equal("elbows flare", composed.Scenes.Single(s => s.Narration.StartsWith("Number two", StringComparison.Ordinal)).Emphasis);
        Assert.Equal("Squeeze your glutes", composed.Scenes.Single(s => s.Narration.StartsWith("Squeeze", StringComparison.Ordinal)).Emphasis);
        Assert.Equal("mistakes", composed.Scenes[1].Emphasis);   // "3 MISTAKES" on the wall
        Assert.Null(composed.Scenes[^1].Emphasis);             // "WHICH ONE?" - never a filler word
    }

    [Fact]
    public void WithoutWrongFormArt_AMistakeIsAReaction_AndThePictureIsWanted()
    {
        var composed = StoryboardComposer.Compose(Countdown(), Catalog, Subjects);

        Assert.Equal(ShotType.MistakeReaction, composed.Scenes[2].Shot);
        Assert.Equal("HIPS TOO HIGH", composed.Scenes[2].OnScreenText);
        Assert.Equal(["plank-hips-high", "pushup-elbows-flare", "pushup-hips-sag"], composed.WantedArt);
    }

    [Fact]
    public void WithTheWrongFormArt_TheMistakeIsASplitOfWrongAndRight_OnTheRightPose()
    {
        var composed = StoryboardComposer.Compose(Countdown(), [.. Catalog, "pushup-elbows-flare"], Subjects);

        var split = composed.Scenes[4];
        Assert.Equal(ShotType.SplitFix, split.Shot);
        Assert.Equal(["pushup-bottom"], split.AssetIds);          // the mistake is about the bottom pose
        Assert.Equal(["pushup-elbows-flare"], split.AltAssetIds);
        Assert.Equal("ELBOWS IN", split.SecondaryText);
        Assert.DoesNotContain("pushup-elbows-flare", composed.WantedArt);
    }

    [Fact]
    public void TheFixBeatsAlternate_AndCarryTheBodyPointAndTheArrow()
    {
        var composed = StoryboardComposer.Compose(Countdown(), Catalog, Subjects);

        Assert.Equal([ShotType.FixDemo, ShotType.ZoomDetail, ShotType.FixDemo], new[] { composed.Scenes[3].Shot, composed.Scenes[5].Shot, composed.Scenes[7].Shot });
        Assert.Equal([0.55, 0.35], composed.Scenes[3].FocusPoint);
        Assert.Equal("down", composed.Scenes[3].Arrow);
        Assert.Equal(["pushup-top"], composed.Scenes[7].AssetIds);
        Assert.Equal(["pushup-bottom"], composed.Scenes[7].AltAssetIds);  // the other pose, for the reps
    }

    [Fact]
    public void Levels_WalkTheLadder_WithALineupHookAndOutro()
    {
        var levels = new Storyboard(StoryboardFormat.Levels, "Which pull-up level are you", "Most people get stuck at level two",
            [new("dead-hang", "you can hold on"), new("chin-up", "you can pull up palms in"), new("pullup", "you own the bar"), new("muscle-up", "you go over it")],
            null, null, "Which level are you at?", "pull");

        var composed = StoryboardComposer.Compose(levels, Catalog, Subjects);

        Assert.Equal(7, composed.Scenes.Count);
        Assert.Equal(ShotType.Lineup, composed.Scenes[0].Shot);
        Assert.Equal(["dead-hang", "chin-up-top", "pullup-top", "muscle-up-top"], composed.Scenes[0].AssetIds);
        Assert.Equal("Level two: The chin-up. You can pull up palms in.", composed.Scenes[3].Narration);
        Assert.Equal(2, composed.Scenes[3].Count);        Assert.Equal(4, composed.Scenes[3].Total);
        Assert.Equal(ShotType.Lineup, composed.Scenes[^1].Shot);
        Assert.Empty(composed.WantedArt);
    }

    [Fact]
    public void ComposingAgain_GivesTheSameNarration_SoNewArtNeverChangesTheVoiceOver()
    {
        var before = StoryboardComposer.Compose(Countdown(), Catalog, Subjects);
        var after = StoryboardComposer.Compose(Countdown(), [.. Catalog, "pushup-hips-sag", "plank-hips-high"], Subjects);

        Assert.Equal(before.Narration, after.Narration);
        Assert.Equal(before.Scenes.Select(s => s.Narration), after.Scenes.Select(s => s.Narration));
    }

    [Fact]
    public void ALevelStressesItsBenchmark_AndALineEndingOnACommaGetsNoStrayPeriod()
    {
        var levels = new Storyboard(StoryboardFormat.Levels, "Which core level are you", "Most people get stuck at one of these levels,",
            [new("plank", "hold a straight-line plank for 60 seconds no wobble"), new("side-plank", "do 20 clean reps with control"), new("leg-raise", "10 slow raises with straight legs"), new("l-sit", "hold")],
            null, null, "Which level are you at?", "core");

        var composed = StoryboardComposer.Compose(levels, Catalog, Subjects);

        Assert.Equal("Most people get stuck at one of these levels.", composed.Scenes[1].Narration);
        Assert.Equal("60 seconds", composed.Scenes[2].Emphasis);
        Assert.Equal("20 clean reps", composed.Scenes[3].Emphasis);
        Assert.Equal("10 slow raises", composed.Scenes[4].Emphasis);
    }

    [Theory]
    [InlineData("a straight-line plank held for 60 seconds", 60, "seconds")]
    [InlineData("8 strict pull-ups", 8, "reps")]
    [InlineData("one clean muscle-up", 1, "reps")]
    [InlineData("hang from the bar", null, null)]
    public void Goal_ReadsTheBenchmarkForTheOnScreenCounter(string benchmark, int? value, string? unit)
    {
        Assert.Equal((value, unit), StoryboardComposer.Goal(benchmark));
    }

    [Fact]
    public void LadderPick_FollowsTheTopicsWords()
    {
        Assert.Equal("core", LevelLadders.Pick("Core Levels Ranked").Id);
        Assert.Equal("push", LevelLadders.Pick("Push-Up Levels: Which One Are You?").Id);
        Assert.Equal("pull", LevelLadders.Pick("What's Your Level?").Id);
    }
}
