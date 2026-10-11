using System.Text.RegularExpressions;

namespace ShortsGenerator.Domain;

/// <summary>One stretch of speech with its own pace, loudness and the silence after it.</summary>
public sealed record SpeechPart(string Text, double Speed = 1.0, double PauseAfter = 0, double Gain = 1.0);

/// <summary>One scene's speech: its parts, and the breath before the next scene.</summary>
public sealed record SpeechSegment(IReadOnlyList<SpeechPart> Parts, double PauseAfter);

public enum EmphasisStyle
{
    None,

    // A small pause right before the key phrase (one synthesis, a comma in the spoken text).
    Pause,

    // The key phrase as its own part: a beat before it, slower and louder.
    Stress,
}

// How the narration is spoken, not just what: the owner found the voice "talking non-stop,
// like a machine". Speech gets the rhythm a person gives it - a beat after the hook, the
// number said on its own ("Number two." ... pause ... the point), the key phrase stressed, a
// slower close - all decided here from the scenes, never by the LLM. The words stay exactly
// the scene's narration (captions and timing rely on that); only commas may be added.
public static partial class SpeechDelivery
{
    public const double HookPause = 0.55;
    public const double IntroPause = 0.4;
    public const double PointPause = 0.35;
    public const double DefaultPause = 0.25;
    public const double LeadInPause = 0.32;

    public static IReadOnlyList<SpeechSegment> Plan(IReadOnlyList<Scene> narratedScenes, EmphasisStyle emphasis)
    {
        var result = new List<SpeechSegment>(narratedScenes.Count);
        for (var i = 0; i < narratedScenes.Count; i++)
        {
            var scene = narratedScenes[i];
            var first = i == 0;
            var last = i == narratedScenes.Count - 1;
            // The hook and the closing question land a touch slower.
            var pace = first || last ? 0.95 : 1.0;
            var parts = new List<SpeechPart>();
            var text = scene.Narration!.Trim();

            var phrase = scene.Emphasis;
            var leadIn = LeadIn().Match(text);
            if (leadIn.Success)
            {
                parts.Add(new SpeechPart(Capitalize(leadIn.Groups["lead"].Value) + ".", 0.9, LeadInPause));
                text = leadIn.Groups["rest"].Value;
                var name = ExerciseName().Match(text);
                if (leadIn.Groups["lead"].Value.StartsWith("Level", StringComparison.OrdinalIgnoreCase) && name.Success)
                {
                    parts.Add(new SpeechPart(name.Groups["name"].Value, 0.94, LeadInPause));
                    text = name.Groups["rest"].Value;
                    // The name was just said on its own; stressing it again later sounds odd.
                    if (phrase is not null && name.Groups["name"].Value.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                    {
                        phrase = null;
                    }
                }
            }

            if (text.Length > 0)
            {
                parts.AddRange(Emphasize(text, phrase, emphasis, pace));
            }

            var pause = first ? HookPause
                : i == 1 ? IntroPause
                : last ? 0.3
                : scene.Shot is ShotType.SplitFix or ShotType.MistakeReaction or ShotType.LevelShow ||
                  scene.Type is SceneType.Comparison or SceneType.MythVsFact ? PointPause
                : DefaultPause;
            result.Add(new SpeechSegment(parts, pause));
        }

        return result;
    }

    private static IEnumerable<SpeechPart> Emphasize(string text, string? phrase, EmphasisStyle style, double pace)
    {
        var at = style == EmphasisStyle.None ? -1 : Find(text, phrase);
        // Only with at least two words before it: stressing the first word sounds odd.
        if (at < 0 || text[..at].Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 2)
        {
            return [new SpeechPart(text, pace)];
        }

        var before = text[..at].TrimEnd(' ', ',');
        var key = text.Substring(at, phrase!.Trim().Length);
        var after = text[(at + key.Length)..];
        if (style == EmphasisStyle.Pause)
        {
            return [new SpeechPart($"{before}, {key}{after}", pace)];
        }

        var parts = new List<SpeechPart>
        {
            new(before, pace, 0.12),
            new(key + (after.Trim().Length <= 1 ? after.Trim() : string.Empty), 0.86, after.Trim().Length <= 1 ? 0 : 0.05, 1.25),
        };
        if (after.Trim().Length > 1)
        {
            parts.Add(new SpeechPart(after.Trim().TrimStart(','), pace));
        }

        return parts;
    }

    /// <summary>Where the phrase starts in the text as whole words (case-insensitive), or -1.</summary>
    private static int Find(string text, string? phrase)
    {
        var p = phrase?.Trim();
        if (string.IsNullOrEmpty(p) || p.Split(' ').Length > 3)
        {
            return -1;
        }

        var m = Regex.Match(text, $@"(?<![\w-]){Regex.Escape(p)}(?![\w-])", RegexOptions.IgnoreCase);
        return m.Success ? m.Index : -1;
    }

    private static string Capitalize(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    [GeneratedRegex(@"^(?<lead>(?:Number|Level) (?:one|two|three|four|five|\d))[:.]\s*(?<rest>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex LeadIn();

    [GeneratedRegex(@"^(?<name>The [^.]{2,40}\.)\s+(?<rest>.+)$")]
    private static partial Regex ExerciseName();
}
