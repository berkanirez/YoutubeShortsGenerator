using System.Text.RegularExpressions;

namespace ShortsGenerator.Domain;

public static class ScriptValidator
{
    // ~25-45s of spoken narration at a natural pace (~140-150 wpm) is roughly 60-110 words.
    // 130 is a hard ceiling so a long outlier can't blow out the video duration.
    public const int MinWords = 40;
    public const int MaxWords = 130;

    // Not an exhaustive medical-safety filter — a last-resort net that rejects the most
    // obvious treatment/diagnosis claims CLAUDE.md explicitly forbids (section 4).
    private static readonly string[] DisallowedPhrases =
    [
        "cure", "cures", "cured",
        "diagnose", "diagnosis",
        "prescribe", "prescription",
        "treats disease", "treatment for",
        "guaranteed results", "guaranteed to",
    ];

    public static ValidationResult Validate(string narration)
    {
        var reasons = new List<string>();

        if (string.IsNullOrWhiteSpace(narration))
        {
            reasons.Add("Narration is empty.");
            return new ValidationResult(false, reasons);
        }

        var wordCount = CountWords(narration);
        if (wordCount < MinWords)
        {
            reasons.Add($"Too short ({wordCount} words; needs at least {MinWords}).");
        }
        else if (wordCount > MaxWords)
        {
            reasons.Add($"Too long ({wordCount} words; must be at most {MaxWords}).");
        }

        foreach (var phrase in DisallowedPhrases)
        {
            if (narration.Contains(phrase, StringComparison.OrdinalIgnoreCase))
            {
                reasons.Add($"Contains a disallowed medical-claim phrase: '{phrase}'.");
            }
        }

        return new ValidationResult(reasons.Count == 0, reasons);
    }

    private static int CountWords(string text) => Regex.Matches(text, @"\S+").Count;
}
