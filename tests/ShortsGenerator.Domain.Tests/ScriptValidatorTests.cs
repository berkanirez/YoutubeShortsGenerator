namespace ShortsGenerator.Domain.Tests;

public class ScriptValidatorTests
{
    [Fact]
    public void Validate_EmptyNarration_IsInvalid()
    {
        var result = ScriptValidator.Validate("   ");

        Assert.False(result.IsValid);
        Assert.Contains(result.Reasons, r => r.Contains("empty", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_TooShortNarration_IsInvalid()
    {
        var narration = string.Join(' ', Enumerable.Repeat("word", ScriptValidator.MinWords - 1));

        var result = ScriptValidator.Validate(narration);

        Assert.False(result.IsValid);
        Assert.Contains(result.Reasons, r => r.Contains("Too short", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_TooLongNarration_IsInvalid()
    {
        var narration = string.Join(' ', Enumerable.Repeat("word", ScriptValidator.MaxWords + 1));

        var result = ScriptValidator.Validate(narration);

        Assert.False(result.IsValid);
        Assert.Contains(result.Reasons, r => r.Contains("Too long", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_WordCountWithinRange_IsValid()
    {
        var narration = string.Join(' ', Enumerable.Repeat("word", ScriptValidator.MinWords));

        var result = ScriptValidator.Validate(narration);

        Assert.True(result.IsValid);
        Assert.Empty(result.Reasons);
    }

    [Theory]
    [InlineData("This will cure your bad knees completely.")]
    [InlineData("We can diagnose your injury from this video.")]
    [InlineData("This routine is a guaranteed results program.")]
    public void Validate_ContainsDisallowedMedicalClaim_IsInvalid(string sentence)
    {
        var narration = sentence + " " + string.Join(' ', Enumerable.Repeat("word", ScriptValidator.MinWords));

        var result = ScriptValidator.Validate(narration);

        Assert.False(result.IsValid);
        Assert.Contains(result.Reasons, r => r.Contains("disallowed medical-claim phrase", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Secure your grip before every rep.")]
    [InlineData("Poor form obscures real progress.")]
    [InlineData("Procure a sturdy bar for your doorway.")]
    [InlineData("Track how fast you recover between sets.")]
    public void Validate_WordsMerelyContainingADisallowedPhrase_AreFine(string sentence)
    {
        var narration = sentence + " " + string.Join(' ', Enumerable.Repeat("word", ScriptValidator.MinWords));

        var result = ScriptValidator.Validate(narration);

        Assert.True(result.IsValid, string.Join("; ", result.Reasons));
    }

    [Fact]
    public void Validate_DisallowedPhraseAtAnyCaseOrNextToPunctuation_IsStillCaught()
    {
        var narration = "Stretching CURES, nothing. " + string.Join(' ', Enumerable.Repeat("word", ScriptValidator.MinWords));

        Assert.False(ScriptValidator.Validate(narration).IsValid);
    }
}
