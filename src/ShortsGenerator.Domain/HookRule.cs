using System.Text.RegularExpressions;

namespace ShortsGenerator.Domain;

// The first sentence of a Short is its hook: viewers decide in about a second whether to
// stay. Measured on the scripts generated so far: only ~3 of 30 opened with one - 11 began
// "When it comes to / When performing / When you're ...", 6 "If you're ...", several with a
// definition ("Running is a high-impact activity ..."). A hook here is short, speaks to the
// viewer or asks them something, and doesn't open with a warm-up phrase.
public static partial class HookRule
{
    public const int MaxHookWords = 14;

    private static readonly string[] WeakOpeners =
    [
        "when it comes to", "when performing", "when you", "when doing", "if you", "as you",
        "to achieve", "to increase", "to get", "in this video", "today", "hey", "welcome",
        "let's", "lets", "many people", "it's no secret", "it is no secret",
    ];

    public static string FirstSentence(string script)
    {
        var match = SentenceEnd().Match(script.Trim());
        return match.Success ? script.Trim()[..(match.Index + match.Length)].Trim() : script.Trim();
    }

    /// <summary>Why the opening isn't a hook (empty when it is one).</summary>
    public static IReadOnlyList<string> Check(string script)
    {
        var first = FirstSentence(script);
        var lower = first.ToLowerInvariant();
        var words = first.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        var reasons = new List<string>();

        if (words > MaxHookWords)
        {
            reasons.Add($"The first sentence is the hook and has {words} words; keep it to {MaxHookWords} or fewer.");
        }

        var opener = WeakOpeners.FirstOrDefault(o => lower.StartsWith(o, StringComparison.Ordinal));
        if (opener is not null)
        {
            reasons.Add($"The first sentence starts with \"{first[..opener.Length]}\", a slow warm-up; open with the hook itself.");
        }
        else if (Definition().IsMatch(lower))
        {
            reasons.Add("The first sentence is a definition; open with a hook instead (a question to the viewer, a surprising claim, or their mistake).");
        }

        if (!first.EndsWith('?') && !Viewer().IsMatch(lower))
        {
            reasons.Add("The first sentence must speak to the viewer: ask them a question or say \"you\"/\"your\".");
        }

        return reasons;
    }

    [GeneratedRegex(@"[.!?](?=\s|$)")]
    private static partial Regex SentenceEnd();

    // "<Something> is a / are a / are an ..." - a textbook opening.
    [GeneratedRegex(@"^[a-z'\- ]{2,40}\b(is|are)\s+(a|an)\s")]
    private static partial Regex Definition();

    [GeneratedRegex(@"\byou(r|'re|'ve|'ll)?\b")]
    private static partial Regex Viewer();
}
