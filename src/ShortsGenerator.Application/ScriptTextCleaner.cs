using System.Text.RegularExpressions;

namespace ShortsGenerator.Application;

public static class ScriptTextCleaner
{
    private static readonly string[] KnownPreambles =
    [
        "here's your script:",
        "here is your script:",
        "narration:",
        "script:",
    ];

    public static string Clean(string raw)
    {
        var text = raw.Trim();
        text = StripKnownPreamble(text);
        text = StripWrappingQuotes(text);
        text = CollapseWhitespace(text);
        return text.Trim();
    }

    private static string StripKnownPreamble(string text)
    {
        foreach (var preamble in KnownPreambles)
        {
            if (text.StartsWith(preamble, StringComparison.OrdinalIgnoreCase))
            {
                return text[preamble.Length..].TrimStart();
            }
        }

        return text;
    }

    private static string StripWrappingQuotes(string text)
    {
        if (text.Length >= 2)
        {
            var first = text[0];
            var last = text[^1];
            var isMatchingQuotePair = (first == '"' && last == '"') || (first == '\'' && last == '\'');
            if (isMatchingQuotePair)
            {
                return text[1..^1].Trim();
            }
        }

        return text;
    }

    private static string CollapseWhitespace(string text) => Regex.Replace(text, @"\s+", " ");
}
