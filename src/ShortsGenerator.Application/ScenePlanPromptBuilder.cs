using System.Text;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Application;

// Since Day 15 the narration arrives already split into numbered scenes (NarrationSegmenter);
// the model only chooses how each scene looks. It never has to reproduce the narration, so
// it can't paraphrase it, and its answer is short enough to never hit the output-token cap.
public static class ScenePlanPromptBuilder
{
    // The JSON skeleton's placeholder values, defined once so the planner can recognise
    // them coming back. Found on Day 22 in the dashboard's plan table: a Hook whose
    // on-screen text was literally "max 5 words" - the model had copied the placeholder.
    // Day 29: in angle brackets. A Hook came back as "max Form" - half of the old "max 5 words"
    // placeholder plus a word of the model's own, which an exact comparison can't catch. Text
    // with "<" or ">" in it is never real on-screen content, so any partial copy is caught too.
    public const string TitlePlaceholder = "<short video title>";
    public const string HookPlaceholder = "<one punchy opening sentence>";
    public const string TakeawayPlaceholder = "<the one specific lesson of this video>";
    public const string OnScreenTextPlaceholder = "<caption>";
    public const string EmphasisPlaceholder = "<key words from this scene>";

    public static IReadOnlyList<string> Placeholders { get; } =
        [TitlePlaceholder, HookPlaceholder, TakeawayPlaceholder, OnScreenTextPlaceholder, EmphasisPlaceholder];

    public static bool IsPlaceholder(string? text) =>
        text is not null &&
        (text.Contains('<') || text.Contains('>') ||
         Placeholders.Any(p => string.Equals(p.Trim('<', '>'), text.Trim(), StringComparison.OrdinalIgnoreCase)));

    public static string Build(
        string topic,
        IReadOnlyList<string> sceneNarrations,
        IReadOnlyList<AssetMetadata> knownAssets,
        IReadOnlyList<string>? previousFailureReasons = null,
        int variationSeed = 0)
    {
        var middleTypes = Enum.GetNames<SceneType>()
            .Where(t => t is not (nameof(SceneType.Hook) or nameof(SceneType.Outro)))
            .ToList();
        var middleSceneTypes = string.Join(", ", Rotate(middleTypes, variationSeed));
        var count = sceneNarrations.Count;

        var sb = new StringBuilder();
        sb.AppendLine("You are the visual director of a short (25-45 second), vertical fitness education video.");
        sb.AppendLine($"Topic: {topic}");
        sb.AppendLine();
        sb.AppendLine($"The narration is already split into {count} scenes, in order:");
        for (var i = 0; i < count; i++)
        {
            sb.AppendLine($"{i + 1}. \"{sceneNarrations[i]}\"");
        }

        sb.AppendLine();
        sb.AppendLine($"Decide how each of the {count} scenes looks. Respond with ONLY one JSON object (no markdown,");
        sb.AppendLine("no code fences, no commentary), with exactly one entry in \"scenes\" per numbered scene, in order:");
        sb.AppendLine($$"""
            {
              "title": "{{TitlePlaceholder}}",
              "hook": "{{HookPlaceholder}}",
              "takeaway": "{{TakeawayPlaceholder}}",
              "musicMood": "upbeat",
              "scenes": [
                { "scene": 1, "type": "Hook", "onScreenText": "{{OnScreenTextPlaceholder}}", "assetIds": [], "animation": "punchZoom", "sfx": null, "emphasis": "{{EmphasisPlaceholder}}", "secondaryText": null, "items": [] }
              ]
            }
            """);
        sb.AppendLine("The values above describe what goes in each field - replace every one of them with real content.");
        sb.AppendLine("Rules:");
        sb.AppendLine($"- \"scenes\" must contain exactly {count} entries, numbered 1 to {count}.");
        sb.AppendLine($"- Scene 1 is type \"Hook\" and scene {count} is type \"Outro\". Every other scene uses one of: {middleSceneTypes}.");
        // No example slogans: llama3.2:3b copied "Full range" into the Hook of 5 videos out of 5.
        sb.AppendLine("- \"onScreenText\": a slogan of 1-3 words painted on the gym wall behind the visual, taken from");
        sb.AppendLine("  THAT scene's own narration. Not a sentence - the captions already show every spoken word.");
        sb.AppendLine("- \"emphasis\": 1-3 words copied exactly from THAT scene's narration (the key idea), or null.");
        sb.AppendLine("- Each type has its own on-screen template; pick the type whose template fits what the scene says");
        sb.AppendLine("  (this list is in no particular order - do not just follow it):");
        foreach (var guide in Rotate(TypeGuides, variationSeed))
        {
            sb.AppendLine(guide);
        }

        sb.AppendLine("  * Hook / Outro: onScreenText only (secondaryText null, items []).");
        sb.AppendLine("  Use at least 3 different types between Hook and Outro, never the same type twice in a row,");
        sb.AppendLine("  and no type more than twice.");

        sb.AppendLine("- Never show the same asset in two consecutive scenes, and no asset more than twice.");
        sb.AppendLine("- Never put a number in onScreenText, secondaryText or items unless THAT scene's narration says it.");
        sb.AppendLine("  Use Progress only for a scene whose narration contains a number.");
        sb.AppendLine("- \"takeaway\": the single, specific lesson of THIS video (not generic advice like \"train hard\").");
        sb.AppendLine("- \"animation\" is required, one of: fade, slide, pop, punchZoom, shake, bounce, pulse, counter, highlight.");
        sb.AppendLine("  Use \"counter\" only when the on-screen text contains a number.");
        sb.AppendLine("- \"sfx\": one of pop, whoosh, error, success, impact, click - or null. Use sound effects");
        sb.AppendLine("  sparingly: at most 3 scenes, only where a sound underlines the moment (e.g. \"error\" on a");
        sb.AppendLine("  common mistake, \"success\" on the fix).");
        sb.AppendLine("- \"musicMood\": \"upbeat\" for training/performance topics, \"calm\" for recovery, sleep or mobility.");
        sb.AppendLine("- \"assetIds\": 0 or 1 ids from this catalog only - never invent one:");
        foreach (var asset in knownAssets)
        {
            sb.AppendLine($"  - {asset.Id} ({asset.Category}: {string.Join(", ", asset.Tags)})");
        }

        if (previousFailureReasons is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("Your previous attempt was rejected for these reasons - fix all of them this time:");
            foreach (var reason in previousFailureReasons)
            {
                sb.AppendLine($"- {reason}");
            }
        }

        return sb.ToString();
    }

    private static readonly string[] TypeGuides =
    [
        // No literal example texts here: on Day 20 the model copied an example myth ("More reps
        // always", from a pull-up example) into a plank video. Describing the shape instead.
        "  * MythVsFact: onScreenText = the common belief THIS scene's narration debunks, secondaryText = what\n" +
        "    is actually true according to the narration (max 5 words each). The card already says MYTH and\n" +
        "    FACT - never write the words \"myth\", \"fact\", \"wrong way\" or \"right way\" in the text.",
        "  * Comparison: onScreenText = the mistake the narration warns about, secondaryText = its fix\n" +
        "    (max 4 words each), both taken from what THIS scene says.",
        "  * List: onScreenText = a short title, items = 2-3 tips of max 4 words each.",
        "  * Recovery: rest, sleep or recovery advice; onScreenText = a short title, items = 2-3 tips.",
        // Day 29: the old example ("10 clean reps") came back word for word in 3 of 5 videos.
        "  * Progress: onScreenText = a number THIS scene's narration mentions plus what it counts\n" +
        "    (max 4 words), secondaryText = what reaching it gives you.",
        "  * ExerciseDemo: how to perform a movement; onScreenText only. Pair it with an exercise asset.",
        "  * AnatomyHighlight: only when the scene is about which muscle does the work, and only with an\n" +
        "    anatomy asset that shows that muscle; onScreenText only.",
    ];

    // Same items, a different starting point per seed - deterministic for one job.
    private static IEnumerable<T> Rotate<T>(IReadOnlyList<T> items, int seed)
    {
        var offset = items.Count == 0 ? 0 : seed % items.Count;
        return items.Skip(offset).Concat(items.Take(offset));
    }
}
