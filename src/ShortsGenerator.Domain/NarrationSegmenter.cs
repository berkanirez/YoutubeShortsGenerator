using System.Text.RegularExpressions;

namespace ShortsGenerator.Domain;

// Splits a narration into the per-scene pieces of a plan - in code, not by the LLM.
//
// Found on Day 15 with the first real end-to-end run: asking llama3.2:3b to copy the
// narration verbatim *and* split it *and* emit 11 fields per scene made it either
// paraphrase (rejected by VideoPlanValidator) or run out of output tokens mid-JSON. The
// split is pure text processing, so it's done here deterministically and the LLM only
// decides what each already-split scene should *look* like.
public static class NarrationSegmenter
{
    // ~4 seconds of speech per scene at Kokoro's ~3.5 words/second: long enough to read
    // the on-screen text, short enough to keep the visuals moving.
    public const int TargetWordsPerScene = 13;

    // Where a sentence ends: . ! or ? (optionally followed by a closing quote/bracket).
    private static readonly Regex SentenceEnd = new(@"[.!?][""')\]]*$", RegexOptions.Compiled);

    // Natural places to break inside a long sentence.
    private static readonly Regex ClauseEnd = new(@"[,;:—–]$", RegexOptions.Compiled);

    // 5, not 3: a real Day 20 plan had a 3-word scene ("By doing so,") split off a longer
    // sentence - about one second on screen, too short to read and meaningless alone.
    private const int MinWordsPerSplitHalf = 5;

    /// <summary>
    /// Returns between <paramref name="minScenes"/> and <paramref name="maxScenes"/> pieces whose
    /// space-joined concatenation is exactly the input (whitespace normalized). Returns fewer
    /// than <paramref name="minScenes"/> only when the text is too short to split further.
    /// </summary>
    public static IReadOnlyList<string> Split(string narration, int minScenes, int maxScenes)
    {
        var words = narration.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return [];
        }

        var pieces = SplitIntoSentences(words);
        var target = Math.Clamp(
            (int)Math.Round(words.Length / (double)TargetWordsPerScene, MidpointRounding.AwayFromZero),
            minScenes,
            maxScenes);

        while (pieces.Count < target && TrySplitLongest(pieces))
        {
        }

        while (pieces.Count > target)
        {
            MergeShortestNeighbours(pieces);
        }

        return pieces.Select(piece => string.Join(' ', piece)).ToList();
    }

    private static List<List<string>> SplitIntoSentences(string[] words)
    {
        var sentences = new List<List<string>>();
        var current = new List<string>();
        foreach (var word in words)
        {
            current.Add(word);
            if (SentenceEnd.IsMatch(word))
            {
                sentences.Add(current);
                current = [];
            }
        }

        if (current.Count > 0)
        {
            sentences.Add(current);
        }

        return sentences;
    }

    // Splits the longest piece in two, preferring a clause boundary (comma, semicolon, dash)
    // nearest its middle; falls back to the middle word. False when nothing is long enough.
    private static bool TrySplitLongest(List<List<string>> pieces)
    {
        var index = pieces.IndexOf(pieces.MaxBy(piece => piece.Count)!);
        var piece = pieces[index];
        if (piece.Count < MinWordsPerSplitHalf * 2)
        {
            return false;
        }

        var middle = piece.Count / 2.0;
        var splitAfter = Enumerable.Range(MinWordsPerSplitHalf - 1, piece.Count - (MinWordsPerSplitHalf * 2) + 1)
            .Where(i => ClauseEnd.IsMatch(piece[i]))
            .OrderBy(i => Math.Abs(i + 1 - middle))
            .Select(i => (int?)i)
            .FirstOrDefault() ?? (int)Math.Floor(middle) - 1;

        pieces[index] = piece[..(splitAfter + 1)];
        pieces.Insert(index + 1, piece[(splitAfter + 1)..]);
        return true;
    }

    private static void MergeShortestNeighbours(List<List<string>> pieces)
    {
        var best = Enumerable.Range(0, pieces.Count - 1).MinBy(i => pieces[i].Count + pieces[i + 1].Count);
        pieces[best] = [.. pieces[best], .. pieces[best + 1]];
        pieces.RemoveAt(best + 1);
    }
}
