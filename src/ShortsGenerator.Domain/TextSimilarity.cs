using System.Text.RegularExpressions;

namespace ShortsGenerator.Domain;

// "Shingle" overlap: the share of a text's n-word phrases that also appear in another text.
// The classic near-duplicate check (search engines use it to find copied pages): unlike a
// whole-text comparison it catches reused sentences inside otherwise new text, and unlike
// a single-word comparison it ignores ordinary shared vocabulary ("your", "pull-ups").
public static partial class TextSimilarity
{
    public static IReadOnlySet<string> Shingles(string text, int size)
    {
        var words = WordPattern().Matches(text.ToLowerInvariant()).Select(m => m.Value).ToArray();
        var shingles = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i + size <= words.Length; i++)
        {
            shingles.Add(string.Join(' ', words, i, size));
        }

        return shingles;
    }

    /// <summary>Fraction (0..1) of <paramref name="candidate"/>'s shingles that also occur in <paramref name="existing"/>.</summary>
    public static double Overlap(string candidate, string existing, int size)
    {
        var candidateShingles = Shingles(candidate, size);
        if (candidateShingles.Count == 0)
        {
            return 0;
        }

        var existingShingles = Shingles(existing, size);
        return candidateShingles.Count(existingShingles.Contains) / (double)candidateShingles.Count;
    }

    // Letters/digits with inner apostrophes/hyphens ("aren't", "pull-ups"); punctuation dropped.
    [GeneratedRegex(@"[a-z0-9]+(?:['\-][a-z0-9]+)*")]
    private static partial Regex WordPattern();
}
