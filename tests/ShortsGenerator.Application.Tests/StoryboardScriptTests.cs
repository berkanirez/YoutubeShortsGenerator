using ShortsGenerator.Domain;

namespace ShortsGenerator.Application.Tests;

public class StoryboardScriptTests
{
    private static readonly ArtSubject[] Subjects =
    [
        new("pushup", ArtKind.ExercisePair, ["push up"], "push-up", Top: "u", Bottom: "d"),
        new("pushup-hips-sag", ArtKind.Mistake, ["sagging hips"], "x", Exercise: "pushup", Pose: "top", Label: "SAGGING HIPS", FixLabel: "SQUEEZE GLUTES", Point: [0.5, 0.4], Arrow: "up"),
        new("pushup-elbows-flare", ArtKind.Mistake, ["flared elbows"], "x", Exercise: "pushup", Pose: "bottom", Label: "FLARED ELBOWS", FixLabel: "ELBOWS IN", Point: [0.2, 0.7], Arrow: "in"),
    ];

    private const string Mistakes = """
        **FORMAT:** mistakes
        HOOK: Your push-ups are hurting your shoulders.
        INTRO: Here are three fixes, and the last one surprises everyone.
        1: pushup-elbows-flare | Your elbows flare out like wings and pinch your shoulders. | Tuck them in to about forty-five degrees.
        2: other | You rush every rep and bounce off the floor. | Lower yourself slowly for three seconds. | RUSHING REPS | SLOW DOWN
        3: pushup-hips-sag | Your hips sag and your lower back takes the load. | Squeeze your glutes and brace like a plank.
        PAYOFF: Fix these and every push-up builds your chest. | Strong chest
        CTA: Which one were you doing?
        """;

    [Fact]
    public void ATextStartingWithFormat_IsAStoryboardScript_EvenWithMarkdownAround()
    {
        Assert.True(StoryboardScript.IsStructured(Mistakes));
        Assert.False(StoryboardScript.IsStructured("Your push-ups are hurting your shoulders. Here is why."));
    }

    [Fact]
    public void Mistakes_AreParsedInOrder_WithOtherItemsKeepingTheirLabels()
    {
        var (storyboard, problems) = StoryboardScript.Parse(Mistakes, "Push-up fixes", Subjects);

        Assert.Empty(problems);
        Assert.Equal(StoryboardFormat.Countdown, storyboard!.Format);
        Assert.Equal(["pushup-elbows-flare", "other", "pushup-hips-sag"], storyboard.Items.Select(i => i.Subject));
        Assert.Equal("RUSHING REPS", storyboard.Items[1].Label);
        Assert.Equal("Strong chest", storyboard.PayoffWord);
    }

    [Fact]
    public void Build_ComposesScenes_AndAnOtherItemGetsAWordShotInsteadOfAnEmptyDemo()
    {
        var (_, composed, problems) = StoryboardScript.Build(Mistakes, "Push-up fixes", Subjects, ["pushup-top", "pushup-bottom", "pushup-elbows-flare"]);

        Assert.Empty(problems);
        Assert.StartsWith("Number one: Your elbows flare out", composed!.Scenes[2].Narration);
        Assert.Equal(ShotType.SplitFix, composed.Scenes[2].Shot);              // drawn wrong form
        Assert.Equal(ShotType.MistakeReaction, composed.Scenes[4].Shot);       // "other": no drawing
        Assert.Equal("RUSHING REPS", composed.Scenes[4].OnScreenText);
        Assert.Equal(ShotType.KineticWord, composed.Scenes[5].Shot);
        Assert.Equal("SLOW DOWN", composed.Scenes[5].OnScreenText);
    }

    [Fact]
    public void Mistakes_UnknownIdsAndMissingLines_AreExplained()
    {
        var bad = Mistakes.Replace("pushup-hips-sag |", "made-up |").Replace("CTA: Which one were you doing?", string.Empty);

        var (storyboard, problems) = StoryboardScript.Parse(bad, "t", Subjects);

        Assert.Null(storyboard);
        Assert.Contains("Item 3: 'made-up' is not a known mistake id (use one from the list, or \"other\").", problems);
        Assert.Contains("The CTA: line is missing.", problems);
    }

    [Fact]
    public void Levels_UseTheNamedLadder()
    {
        const string levels = """
            FORMAT: levels
            LADDER: push
            HOOK: Which push level are you really at?
            INTRO: Most people get stuck at level two.
            1: Can you do twenty clean ones with your chest to the floor?
            2: Ten full dips with your shoulders kept down.
            3: Hold it against a wall for thirty seconds.
            CTA: Which level are you at?
            """;

        var (storyboard, problems) = StoryboardScript.Parse(levels, "t", Subjects);

        Assert.Empty(problems);
        Assert.Equal("push", storyboard!.Ladder);
        Assert.Equal(["pushup", "dip", "handstand"], storyboard.Items.Select(i => i.Subject));
    }

    [Fact]
    public async Task CreatingAJob_WithABrokenStoryboardScript_FailsRightAway()
    {
        var service = new VideoJobService(new InMemoryVideoJobRepository(), TimeProvider.System, new Catalog(Subjects));

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreateJobAsync("Push-up fixes", "FORMAT: mistakes\nHOOK: Your push-ups hurt.", CancellationToken.None));

        Assert.Contains("Your storyboard script can't be used", ex.Message);
    }

    [Fact]
    public void TheChatGptPrompt_ListsTheMistakeIdsAndLadders()
    {
        var prompt = StoryboardScript.ChatGptPrompt(Subjects);

        Assert.Contains("pushup-hips-sag (sagging hips -> squeeze glutes)", prompt);
        Assert.Contains("FORMAT: levels", prompt);
        Assert.Contains("pull: 1 dead hang", prompt);
    }

    private sealed class Catalog(params ArtSubject[] subjects) : IArtSubjectCatalog
    {
        public Task<IReadOnlyList<ArtSubject>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ArtSubject>>(subjects);
    }
}
