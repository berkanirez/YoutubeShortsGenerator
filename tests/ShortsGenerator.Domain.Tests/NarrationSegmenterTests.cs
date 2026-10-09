namespace ShortsGenerator.Domain.Tests;

public class NarrationSegmenterHookTests
{
    [Fact]
    public void AShortFirstSentence_KeepsTheHookSceneToItself()
    {
        // Merging pairs the two shortest neighbours; the 4-word hook + next sentence used to win.
        const string narration =
            "Are your pull-ups stuck? It is likely your technique. " +
            "Start every rep from a dead hang with your arms fully straight below the bar each time. " +
            "Pull until your chin clears the bar and squeeze your back hard at the very top of it. " +
            "Lower yourself slowly instead of dropping down as fast as you possibly can every rep.";

        var pieces = NarrationSegmenter.Split(narration, minScenes: 3, maxScenes: 4);

        Assert.Equal("Are your pull-ups stuck?", pieces[0]);
    }
}

public class NarrationSegmenterTests
{
    // The real script from the first end-to-end run (Day 15): 89 words, only 5 sentences.
    private const string RealScript =
        "If you're finding that your pull-ups aren't increasing, it's likely because your technique needs work. " +
        "Are you starting at the top of the pull-up, squeezing at the top, and fully extending at the bottom? " +
        "If not, it's time to revisit the basics. Make sure you're using a weight that allows you to maintain " +
        "proper form, and focus on slow, controlled movements. Additionally, incorporating variation into your " +
        "routine, such as using different grip positions or adjusting the angle of the pull-up bar, can help " +
        "challenge your body and promote progress.";

    [Fact]
    public void Split_ConcatenationIsAlwaysExactlyTheInput()
    {
        var pieces = NarrationSegmenter.Split(RealScript, 6, 10);

        Assert.Equal(RealScript, string.Join(' ', pieces));
    }

    [Fact]
    public void Split_FiveSentenceScript_IsSplitAtClausesToReachTheTarget()
    {
        var pieces = NarrationSegmenter.Split(RealScript, 6, 10);

        // 89 words / 13 per scene ~= 7 scenes.
        Assert.Equal(7, pieces.Count);
        Assert.All(pieces, piece => Assert.True(piece.Split(' ').Length >= 5, $"too short: '{piece}'"));
    }

    [Fact]
    public void Split_PrefersSentenceThenClauseBoundaries()
    {
        var pieces = NarrationSegmenter.Split(RealScript, 6, 10);

        // Every piece ends at a sentence or clause boundary - no mid-phrase cuts were needed.
        Assert.All(pieces, piece => Assert.Matches(@"[.?!,;]$", piece));
    }

    [Fact]
    public void Split_ManyShortSentences_AreMergedDownToTheWordBasedTarget()
    {
        // 30 sentences x 4 words = 120 words -> 120 / 13 ~= 9 scenes.
        var text = string.Join(' ', Enumerable.Range(1, 30).Select(i => $"Rep number {i} counts."));

        var pieces = NarrationSegmenter.Split(text, 6, 10);

        Assert.Equal(9, pieces.Count);
        Assert.Equal(text, string.Join(' ', pieces));
    }

    [Fact]
    public void Split_VeryLongScript_IsCappedAtTheMaximum()
    {
        // 60 x 4 = 240 words -> 18 by word count, capped to 10.
        var text = string.Join(' ', Enumerable.Range(1, 60).Select(i => $"Rep number {i} counts."));

        Assert.Equal(10, NarrationSegmenter.Split(text, 6, 10).Count);
    }

    [Fact]
    public void Split_SixShortSentences_StaysOneSentencePerScene()
    {
        const string text = "Pull ups are a great back exercise. Keep your core tight throughout the movement. " +
            "Pull your chest towards the bar, not just your chin. Lower yourself under control every single rep. " +
            "Add weight once you can do ten clean reps. Track your progress every single week.";

        var pieces = NarrationSegmenter.Split(text, 6, 10);

        Assert.Equal(6, pieces.Count);
        Assert.Equal("Pull ups are a great back exercise.", pieces[0]);
        Assert.Equal("Track your progress every single week.", pieces[5]);
    }

    [Fact]
    public void Split_LongSentenceWithoutPunctuation_FallsBackToTheMiddleWord()
    {
        var text = string.Join(' ', Enumerable.Range(1, 80).Select(i => $"w{i}")) + ".";

        var pieces = NarrationSegmenter.Split(text, 6, 10);

        Assert.Equal(6, pieces.Count);
        Assert.Equal(text, string.Join(' ', pieces));
    }

    [Fact]
    public void Split_TooShortToReachTheMinimum_ReturnsWhatItCan()
    {
        var pieces = NarrationSegmenter.Split("Rest well tonight.", 6, 10);

        Assert.Equal(["Rest well tonight."], pieces);
    }

    [Fact]
    public void Split_NormalizesWhitespace()
    {
        var pieces = NarrationSegmenter.Split("  One   two three four five six.\nSeven eight nine ten eleven twelve.  ", 1, 10);

        Assert.Equal("One two three four five six. Seven eight nine ten eleven twelve.", string.Join(' ', pieces));
    }
}
