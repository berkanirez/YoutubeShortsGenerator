namespace ShortsGenerator.Domain.Tests;

public class SpeechDeliveryTests
{
    private static Scene S(int order, string narration, ShotType? shot = null, string? emphasis = null, SceneType type = SceneType.List) =>
        new($"s{order}", order, type, 3, narration, null, [], AnimationId.Fade, null, null, emphasis, Shot: shot);

    [Fact]
    public void TheNumberIsSaidOnItsOwn_ThenAPause_ThenThePoint()
    {
        var plan = SpeechDelivery.Plan([S(1, "Your squats hurt."), S(2, "Here are three."), S(3, "Number two: Your heels lift off the floor.", ShotType.SplitFix), S(4, "Which one?")], EmphasisStyle.None);

        var parts = plan[2].Parts;
        Assert.Equal("Number two.", parts[0].Text);
        Assert.Equal(SpeechDelivery.LeadInPause, parts[0].PauseAfter);
        Assert.Equal("Your heels lift off the floor.", parts[1].Text);
        Assert.Equal(SpeechDelivery.PointPause, plan[2].PauseAfter);
    }

    [Fact]
    public void ALevelGetsItsNumberAndItsExerciseAsBeats()
    {
        var plan = SpeechDelivery.Plan([S(1, "Hook here now."), S(2, "Level two: The chin-up. Five from a dead hang.", ShotType.LevelShow)], EmphasisStyle.None);

        Assert.Equal(["Level two.", "The chin-up.", "Five from a dead hang."], plan[1].Parts.Select(p => p.Text));
    }

    [Fact]
    public void ALevelNeverStressesItsExerciseNameTwice()
    {
        var plan = SpeechDelivery.Plan(
            [S(1, "Hook here now."), S(2, "Level one: The plank. Hold a straight-line plank for 60 seconds.", ShotType.LevelShow, "plank")],
            EmphasisStyle.Pause);

        Assert.Equal("Hold a straight-line plank for 60 seconds.", plan[1].Parts[^1].Text);
    }

    [Fact]
    public void TheHookLandsWithABeatAfterIt_AndTheCloseIsSlower()
    {
        var plan = SpeechDelivery.Plan([S(1, "Your squats are wrecking your knees."), S(2, "Here is why."), S(3, "Which one were you doing?")], EmphasisStyle.None);

        Assert.Equal(SpeechDelivery.HookPause, plan[0].PauseAfter);
        Assert.Equal(0.95, plan[0].Parts[0].Speed);
        Assert.Equal(0.95, plan[2].Parts[0].Speed);
    }

    [Fact]
    public void PauseEmphasis_AddsOnlyACommaBeforeTheKeyPhrase()
    {
        var plan = SpeechDelivery.Plan([S(1, "Hook line here."), S(2, "Push your knees out over your toes.", emphasis: "knees out")], EmphasisStyle.Pause);

        Assert.Equal("Push your, knees out over your toes.", Assert.Single(plan[1].Parts).Text);
    }

    [Fact]
    public void StressEmphasis_SaysTheKeyPhraseSlowerAndLouder()
    {
        var plan = SpeechDelivery.Plan([S(1, "Hook line here."), S(2, "Keep your whole back flat.", emphasis: "flat")], EmphasisStyle.Stress);

        var parts = plan[1].Parts;
        Assert.Equal(["Keep your whole back", "flat."], parts.Select(p => p.Text));
        Assert.True(parts[1].Speed < 1 && parts[1].Gain > 1);
    }

    [Fact]
    public void EmphasisNeverStressesTheFirstWord_OrAPhraseTheSentenceDoesNotSay()
    {
        var plan = SpeechDelivery.Plan([S(1, "Hook line here."), S(2, "Squeeze hard every rep.", emphasis: "Squeeze"), S(3, "Go slow.", emphasis: "knees out")], EmphasisStyle.Pause);

        Assert.Equal("Squeeze hard every rep.", plan[1].Parts.Single().Text);
        Assert.Equal("Go slow.", plan[2].Parts.Single().Text);
    }
}
