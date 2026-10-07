namespace ShortsGenerator.Application.Tests;

public class ScriptTextCleanerTests
{
    [Fact]
    public void Clean_StripsWrappingDoubleQuotes()
    {
        var result = ScriptTextCleaner.Clean("\"Train smart, not just hard.\"");

        Assert.Equal("Train smart, not just hard.", result);
    }

    [Fact]
    public void Clean_StripsWrappingSingleQuotes()
    {
        var result = ScriptTextCleaner.Clean("'Train smart, not just hard.'");

        Assert.Equal("Train smart, not just hard.", result);
    }

    [Theory]
    [InlineData("Here's your script:\n\nTrain smart, not just hard.", "Train smart, not just hard.")]
    [InlineData("Script: Train smart, not just hard.", "Train smart, not just hard.")]
    [InlineData("Narration:\nTrain smart, not just hard.", "Train smart, not just hard.")]
    public void Clean_RemovesKnownPreambles(string raw, string expected)
    {
        var result = ScriptTextCleaner.Clean(raw);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Clean_CollapsesExtraWhitespaceAndNewlines()
    {
        var result = ScriptTextCleaner.Clean("Train   smart,\n\nnot just   hard.");

        Assert.Equal("Train smart, not just hard.", result);
    }

    [Fact]
    public void Clean_TrimsLeadingAndTrailingWhitespace()
    {
        var result = ScriptTextCleaner.Clean("   Train smart, not just hard.   ");

        Assert.Equal("Train smart, not just hard.", result);
    }
}
