using ShortsGenerator.Domain;

namespace ShortsGenerator.Application.Tests;

public class StoryboardServiceTests
{
    private static readonly ArtSubject[] Subjects =
    [
        new("pushup", ArtKind.ExercisePair, ["push ups", "push up", "pushups"], "push-up", Top: "u", Bottom: "d"),
        new("pullup", ArtKind.ExercisePair, ["pull ups", "pull up", "pullups"], "pull-up", Top: "u", Bottom: "d"),
        new("squat", ArtKind.ExercisePair, ["squats", "squat"], "squat", Top: "u", Bottom: "d"),
        new("pushup-hips-sag", ArtKind.Mistake, ["sagging hips"], "x", Exercise: "pushup", Label: "A", FixLabel: "B", Point: [0.5, 0.5]),
        new("pullup-kipping", ArtKind.Mistake, ["swinging"], "x", Exercise: "pullup", Label: "A", FixLabel: "B", Point: [0.5, 0.5]),
        new("squat-half", ArtKind.Mistake, ["half squats"], "x", Exercise: "squat", Label: "A", FixLabel: "B", Point: [0.5, 0.5]),
    ];

    [Theory]
    [InlineData("3 Push-Up Mistakes Killing Your Gains", StoryboardFormat.Countdown)]
    [InlineData("Core Levels Ranked - Which One Are You?", StoryboardFormat.Levels)]
    [InlineData("Why Your Squats Hurt Your Knees", StoryboardFormat.Countdown)]
    [InlineData("3 Gym Mistakes Killing Your Gains", StoryboardFormat.Countdown)]
    public void ChooseFormat_FollowsTheTopic(string topic, StoryboardFormat expected)
    {
        Assert.Equal(expected, StoryboardService.ChooseFormat(topic, [], Subjects));
    }

    [Theory]
    [InlineData("Is Cardio Bad for Muscle Growth?")]
    [InlineData("What Happens When You Dead Hang Every Day")]
    [InlineData("Do This and Get Shredded")]                 // fat loss, not technique
    [InlineData("3 Diet Mistakes Keeping You Fat")]           // mistakes, but not exercise form
    [InlineData("Why You Need More Sleep for Gains")]
    [InlineData("Build Bigger Forearms")]                     // no mistake list for it yet
    public void ChooseFormat_TopicsThatAreNotExerciseTechnique_KeepTheClassicPath(string topic)
    {
        Assert.Null(StoryboardService.ChooseFormat(topic, [], Subjects));
    }

    [Fact]
    public void ChooseFormat_AfterTwoCountdowns_ATopicNamingALadderBecomesLevels()
    {
        string[] recent = ["Number three: x. Number two: y.", "Number three: a. Number two: b."];

        Assert.Equal(StoryboardFormat.Levels, StoryboardService.ChooseFormat("Pull-up strength", recent, Subjects));
        Assert.Equal(StoryboardFormat.Countdown, StoryboardService.ChooseFormat("Pull-up strength", [], Subjects));
    }

    private const string ValidCountdown = """
        { "hook": "Your push-ups are hurting your back", "intro": "Three mistakes, and number one surprises everyone",
          "mistakes": [
            { "mistake": "pushup-head-drop", "problem": "your head hangs down toward the floor", "fix": "keep your neck in line with your back" },
            { "mistake": "pushup-elbows-flare", "problem": "your elbows flare out like wings", "fix": "tuck them close to your ribs" },
            { "mistake": "pushup-hips-sag", "problem": "your hips sag toward the floor", "fix": "squeeze your glutes the whole set" } ],
          "payoff": "Fix these and every rep builds muscle", "payoffWord": "Clean reps", "cta": "Which one were you doing?" }
        """;

    private static readonly HashSet<string> Allowed = ["pushup-head-drop", "pushup-elbows-flare", "pushup-hips-sag", "pushup-half-rep"];

    [Fact]
    public void ParseCountdown_AValidAnswer_IsAStoryboardInSpokenOrder()
    {
        var (storyboard, problems) = StoryboardService.ParseCountdown(ValidCountdown, Allowed);

        Assert.Empty(problems);
        Assert.Equal(["pushup-head-drop", "pushup-elbows-flare", "pushup-hips-sag"], storyboard!.Items.Select(i => i.Subject));
        Assert.Equal("Clean reps", storyboard.PayoffWord);
    }

    [Fact]
    public void ParseCountdown_UnknownOrRepeatedMistakes_AreRejectedWithReasons()
    {
        var bad = ValidCountdown.Replace("pushup-head-drop", "pushup-hips-sag");

        var (storyboard, problems) = StoryboardService.ParseCountdown(bad, Allowed);

        Assert.Null(storyboard);
        Assert.Contains("The 3 mistakes must all be different.", problems);

        var (_, unknown) = StoryboardService.ParseCountdown(ValidCountdown.Replace("pushup-head-drop", "made-up"), Allowed);
        Assert.Contains("'made-up' is not one of the listed mistake ids.", unknown);
    }

    [Fact]
    public void ParseCountdown_ATwoMistakeCountdown_NeedsExactlyTwo()
    {
        var (_, problems) = StoryboardService.ParseCountdown(ValidCountdown, Allowed, count: 2);

        Assert.Contains("Give exactly 2 mistakes (got 3).", problems);
    }

    [Fact]
    public void ParseCountdown_ALongHook_IsRejected()
    {
        var bad = ValidCountdown.Replace("Your push-ups are hurting your back", "If you are someone who does push-ups every single day you might be making mistakes");

        var (_, problems) = StoryboardService.ParseCountdown(bad, Allowed);

        Assert.Contains(problems, p => p.StartsWith("\"hook\" has", StringComparison.Ordinal));
    }

    [Fact]
    public void ParseLevels_NeedsOneLinePerStepOfTheLadder()
    {
        var ladder = LevelLadders.Find("push")!;
        var raw = """{ "hook": "Which push level are you", "intro": "Most people stop at level two", "levels": [ { "line": "you can do clean reps" }, { "line": "you press your whole body" } ], "cta": "Which level are you at?" }""";

        var (storyboard, problems) = StoryboardService.ParseLevels(raw, ladder);

        Assert.Null(storyboard);
        Assert.Contains("Give exactly 3 levels (got 2).", problems);
    }
}
