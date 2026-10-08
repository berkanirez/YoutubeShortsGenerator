namespace ShortsGenerator.Domain.Tests;

public class TextSimilarityTests
{
    private const string Earlier =
        "If you're stuck at the same pull-up count, it's probably your form. Keep your core tight and avoid swinging.";

    [Fact]
    public void Overlap_IdenticalText_IsOne()
    {
        Assert.Equal(1.0, TextSimilarity.Overlap(Earlier, Earlier, 5));
    }

    [Fact]
    public void Overlap_IgnoresCaseAndPunctuation()
    {
        Assert.Equal(1.0, TextSimilarity.Overlap(Earlier.ToUpperInvariant().Replace(",", ""), Earlier, 5));
    }

    [Fact]
    public void Overlap_SharedVocabularyAlone_IsNotSimilarity()
    {
        const string different = "Your pull-up count grows when you rest well, eat enough protein and train your back twice a week.";

        Assert.True(TextSimilarity.Overlap(different, Earlier, 5) < 0.05);
    }

    [Fact]
    public void Overlap_ReusedSentenceInsideNewText_IsDetected()
    {
        const string mixed = "Rest days matter more than you think. Keep your core tight and avoid swinging. Sleep is training too.";

        Assert.InRange(TextSimilarity.Overlap(mixed, Earlier, 5), 0.2, 0.6);
    }

    [Fact]
    public void Overlap_TextShorterThanTheShingle_IsZero()
    {
        Assert.Equal(0, TextSimilarity.Overlap("Rest well", Earlier, 5));
    }

    [Fact]
    public void Shingles_KeepContractionsAndHyphenatedWords()
    {
        Assert.Contains("you're stuck at", TextSimilarity.Shingles("If you're stuck at", 3));
        Assert.Contains("same pull-up count", TextSimilarity.Shingles("the same pull-up count", 3));
    }
}
