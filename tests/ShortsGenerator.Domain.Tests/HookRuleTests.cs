namespace ShortsGenerator.Domain.Tests;

// The first sentences below are real: openings of the scripts generated so far. Viewers swipe
// away in the first second, so sentence one has to be a hook.
public class HookRuleTests
{
    private const string Rest = " The rest of the script explains the idea in a few more sentences.";

    [Theory]
    [InlineData("When it comes to building muscle, push-ups are a great starting point.")]
    [InlineData("When performing squats, it's essential to maintain proper breathing.")]
    [InlineData("When you're pushing yourself to new limits in the gym, it's normal to feel sore.")]
    [InlineData("If you're struggling to increase your pull-up numbers, it might be because your form is off.")]
    [InlineData("Running is a high-impact activity that can put repetitive stress on your joints.")]
    [InlineData("Tempo reps are a type of repetition that targets the muscle with varying intensity.")]
    [InlineData("Rest days are just as important as workout days.")]
    public void WeakOpenings_AreRejected(string firstSentence)
    {
        Assert.NotEmpty(HookRule.Check(firstSentence + Rest));
    }

    [Theory]
    [InlineData("Can you build muscle with push-ups?")]
    [InlineData("Your pull-ups are stuck for one simple reason.")]
    [InlineData("Stop doing crunches if you want visible abs.")]
    [InlineData("Is running secretly killing your gains?")]
    public void RealHooks_Pass(string firstSentence)
    {
        Assert.Empty(HookRule.Check(firstSentence + Rest));
    }

    [Fact]
    public void ALongFirstSentence_IsRejectedWithItsWordCount()
    {
        var reasons = HookRule.Check("Are you really running every day and wondering why you are not seeing the gains you expected from it?" + Rest);

        Assert.Contains(reasons, r => r.Contains("19 words"));
    }

    [Fact]
    public void FirstSentence_IsWhateverEndsAtTheFirstFullStopQuestionOrExclamationMark()
    {
        Assert.Equal("Stop skipping rest days!", HookRule.FirstSentence("Stop skipping rest days! Here is why."));
    }
}
