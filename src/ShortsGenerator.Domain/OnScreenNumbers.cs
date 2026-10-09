using System.Text.RegularExpressions;

namespace ShortsGenerator.Domain;

// Day 29: across 15 real videos, 25 numbers were shown on screen that the scene's narration
// never said - "60 minutes of rest = 1% muscle growth", "90%+ proper form" among them. A
// number on screen reads as a fact; CLAUDE.md section 4 doesn't allow invented ones. Rule:
// every number in a scene's on-screen text (title, second text, items) must occur in that
// scene's narration, written as digits or as words ("three to five" = "3-5").
public static partial class OnScreenNumbers
{
    private static readonly Dictionary<string, string> NumberWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zero"] = "0", ["one"] = "1", ["two"] = "2", ["three"] = "3", ["four"] = "4", ["five"] = "5",
        ["six"] = "6", ["seven"] = "7", ["eight"] = "8", ["nine"] = "9", ["ten"] = "10", ["eleven"] = "11",
        ["twelve"] = "12", ["fifteen"] = "15", ["twenty"] = "20", ["thirty"] = "30", ["forty"] = "40",
        ["fifty"] = "50", ["sixty"] = "60", ["ninety"] = "90", ["hundred"] = "100",
        ["single"] = "1", ["once"] = "1", ["twice"] = "2", ["double"] = "2", ["half"] = "50",
    };

    /// <summary>The numbers shown on screen (as shown, e.g. "1%") that the narration never says.</summary>
    public static IReadOnlyList<string> Unsourced(Scene scene)
    {
        var said = SaidNumbers(scene.Narration);
        return Shown(scene)
            .SelectMany(text => NumberPattern().Matches(text).Select(m => m.Value))
            .Where(n => !said.Contains(Bare(n)))
            .Distinct()
            .ToList();
    }

    /// <summary>The scene without the on-screen fields that contain an unsourced number.</summary>
    public static Scene Strip(Scene scene)
    {
        var said = SaidNumbers(scene.Narration);
        bool Clean(string? text) => text is null || NumberPattern().Matches(text).All(m => said.Contains(Bare(m.Value)));
        var items = scene.Items?.Where(Clean).ToList();
        return scene with
        {
            OnScreenText = Clean(scene.OnScreenText) ? scene.OnScreenText : null,
            SecondaryText = Clean(scene.SecondaryText) ? scene.SecondaryText : null,
            Items = items is { Count: > 0 } ? items : null,
        };
    }

    private static IEnumerable<string> Shown(Scene scene) =>
        new[] { scene.OnScreenText, scene.SecondaryText }.Concat(scene.Items ?? []).OfType<string>();

    private static HashSet<string> SaidNumbers(string? narration)
    {
        var said = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(narration))
        {
            return said;
        }

        foreach (Match m in NumberPattern().Matches(narration))
        {
            said.Add(Bare(m.Value));
        }

        foreach (Match m in WordPattern().Matches(narration))
        {
            if (NumberWords.TryGetValue(m.Value, out var digits))
            {
                said.Add(digits);
            }
        }

        return said;
    }

    private static string Bare(string number) => number.TrimEnd('%').TrimStart('0') is { Length: > 0 } n ? n : "0";

    [GeneratedRegex(@"\d+(?:[.,]\d+)?%?")]
    private static partial Regex NumberPattern();

    [GeneratedRegex(@"[A-Za-z]+")]
    private static partial Regex WordPattern();
}
