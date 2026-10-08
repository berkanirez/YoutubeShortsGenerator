using System.Text.RegularExpressions;

namespace ShortsGenerator.Domain;

public static class ScriptValidator
{
    // Calibrated on Day 15 against the real voice: Kokoro speaks ~3.5 words/second (measured
    // Day 12), so the old 40-130 range meant ~11-37s videos - a 58-word sample was only 17s.
    // The prompt aims for 85-120 words (~25-35s, inside CLAUDE.md's 25-45s); the hard limits
    // leave the model some slack (~19-40s) instead of burning retries on a few words.
    public const int MinWords = 65;
    public const int MaxWords = 140;
    public const int TargetMinWords = 85;
    public const int TargetMaxWords = 120;

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
            // Whole words only: a plain Contains("cure") also rejected "secure your grip".
            if (Regex.IsMatch(narration, $@"\b{Regex.Escape(phrase)}\b", RegexOptions.IgnoreCase))
            {
                reasons.Add($"Contains a disallowed medical-claim phrase: '{phrase}'.");
            }
        }

        return new ValidationResult(reasons.Count == 0, reasons);
    }

    private static int CountWords(string text) => Regex.Matches(text, @"\S+").Count;
}
