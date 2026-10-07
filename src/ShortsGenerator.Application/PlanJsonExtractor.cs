using System.Text.RegularExpressions;

namespace ShortsGenerator.Application;

public static class PlanJsonExtractor
{
    // Small local models frequently write the literal string "null" instead of the JSON
    // null token for an empty optional field (e.g. "animation": "null"). That breaks
    // enum parsing (there's no AnimationId member called "null"). Only a property value
    // that is EXACTLY "null" (nothing else inside the quotes) is normalized, so real
    // narration text that happens to contain the word "null" is never touched.
    private static readonly Regex StringNullLiteralPattern =
        new("""(?<=:\s*)"null"(?=\s*[,}\]])""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string Extract(string raw)
    {
        var text = StripCodeFence(raw.Trim());
        text = ExtractJsonObject(text);
        return NormalizeNullStringLiterals(text);
    }

    private static string NormalizeNullStringLiterals(string text) => StringNullLiteralPattern.Replace(text, "null");

    private static string StripCodeFence(string text)
    {
        if (!text.StartsWith("```", StringComparison.Ordinal))
        {
            return text;
        }

        var firstNewline = text.IndexOf('\n');
        if (firstNewline >= 0)
        {
            text = text[(firstNewline + 1)..];
        }

        var closingFenceIndex = text.LastIndexOf("```", StringComparison.Ordinal);
        if (closingFenceIndex >= 0)
        {
            text = text[..closingFenceIndex];
        }

        return text.Trim();
    }

    private static string ExtractJsonObject(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');

        // If braces aren't found in a sane order, return the text as-is and let JSON
        // parsing fail with its own clear error rather than guessing further.
        return start < 0 || end < 0 || end < start ? text : text[start..(end + 1)];
    }
}
