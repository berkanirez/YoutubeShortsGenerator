using System.Text.RegularExpressions;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Application;

// A deterministic second opinion on the model's asset choice, driven only by catalog
// metadata (tags + compatible scene types). Found on Day 20: with core-highlight in the
// catalog, the model still put biceps-highlight on a scene titled "Core Muscle". If the
// chosen asset's tags have nothing to do with what the scene says, and another compatible
// asset's tags do, the better match wins. Well-tagged new assets therefore improve
// selection with no code change - the point of the asset system (CLAUDE.md section 10).
//
// Day 29, from five real videos: a squat drawing in a pull-up video (kept because squat-top
// is tagged "top" and the narration said "...at the top"), biceps for "chest muscles" (kept
// because biceps-highlight is tagged "muscle"), and pull-up drawings never matching "pull-ups"
// (tag "pullup" vs. the words "pull" + "up"). Hence: generic words don't count as a match,
// hyphenated words also match their joined form, the video's topic is the second clue, and an
// anatomy scene never shows a muscle the scene isn't about.
public static partial class AssetMatcher
{
    // Tag words that describe a pose or a category, not what a scene is about.
    private static readonly HashSet<string> GenericWords = new(StringComparer.Ordinal)
    {
        "top", "bottom", "position", "front", "standing", "depth", "hold", "muscle", "equipment",
        "weight", "intro", "neutral", "idle", "mascot",
    };

    public static IReadOnlyList<string> Improve(
        IReadOnlyList<string> chosen, SceneType sceneType, string sceneText, IReadOnlyList<AssetMetadata> catalog, string topic = "")
    {
        var sceneWords = Words(sceneText);
        var choice = Choose(chosen, sceneType, sceneText, sceneWords, catalog, topic);
        return sceneType is SceneType.ExerciseDemo ? PreferTopicExercise(choice, sceneWords, catalog, topic) : choice;
    }

    private static IReadOnlyList<string> Choose(
        IReadOnlyList<string> chosen, SceneType sceneType, string sceneText, IReadOnlySet<string> sceneWords,
        IReadOnlyList<AssetMetadata> catalog, string topic)
    {
        var current = Find(catalog, chosen);
        if (current is not null && Score(current, sceneWords) > 0)
        {
            return chosen;
        }

        // The scene's own words first; the topic only when the scene itself gives no clue
        // ("squeezing your lats at the top" in a pull-up video).
        var best = BestMatch(catalog, sceneType, sceneWords) ?? BestMatch(catalog, sceneType, Words($"{sceneText} {topic}"));
        if (best is not null)
        {
            return [best.Id];
        }

        // A muscle highlight that names a muscle the scene isn't about teaches something false;
        // no asset means the renderer's neutral mascot instead.
        if (sceneType is SceneType.AnatomyHighlight && current is not null && Score(current, Words($"{sceneText} {topic}")) == 0)
        {
            return [];
        }

        // Otherwise no asset at all is a choice the renderer handles (mascot fallback), so a
        // scene without a chosen asset only gets one when something actually matches.
        return chosen;
    }

    // An exercise demo in a push-up video shows push-ups unless the scene itself names the
    // exercise it shows - whatever the choice so far, including none (round 2: "...push back up"
    // matched pull-up's "back" tag; Day 30: "...engage your core" found plank in a pull-up
    // video). An asset's first tag is its subject by catalog convention (docs/ASSETS.md).
    private static IReadOnlyList<string> PreferTopicExercise(
        IReadOnlyList<string> choice, IReadOnlySet<string> sceneWords, IReadOnlyList<AssetMetadata> catalog, string topic)
    {
        var current = Find(catalog, choice);
        if (current is not null && Names(current, sceneWords))
        {
            return choice;
        }

        var topicWords = Words(topic);
        var topicExercise = catalog
            .Where(a => a.CompatibleSceneTypes.Contains(SceneType.ExerciseDemo) && Names(a, topicWords))
            .OrderByDescending(a => Score(a, sceneWords))
            .ThenBy(a => a.Id, StringComparer.Ordinal)
            .FirstOrDefault();
        return topicExercise is not null && (current is null || !Names(current, topicWords)) ? [topicExercise.Id] : choice;
    }

    private static AssetMetadata? Find(IReadOnlyList<AssetMetadata> catalog, IReadOnlyList<string> ids) =>
        ids.Count > 0 ? catalog.FirstOrDefault(a => string.Equals(a.Id, ids[0], StringComparison.OrdinalIgnoreCase)) : null;
    // Day 29: every one of the six plan rejections in the five sample videos was an asset
    // repeated in consecutive scenes or used a third time - each costing a ~13s LLM attempt.
    // Repaired here instead, in scene order: a repeat gets the best other compatible asset that
    // fits the scene (or the topic), or none (the renderer's mascot) when nothing else fits.
    public static IReadOnlyList<Scene> Spread(IReadOnlyList<Scene> scenes, IReadOnlyList<AssetMetadata> catalog, string topic)
    {
        var uses = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var result = new List<Scene>(scenes.Count);
        string? previous = null;
        foreach (var scene in scenes.OrderBy(s => s.Order))
        {
            var asset = scene.AssetIds.FirstOrDefault();
            var repeats = asset is not null &&
                (string.Equals(asset, previous, StringComparison.OrdinalIgnoreCase) ||
                 uses.GetValueOrDefault(asset) >= PlanVariationRules.MaxUsesPerAsset);
            var updated = scene;
            if (repeats)
            {
                bool Free(AssetMetadata a) =>
                    !string.Equals(a.Id, previous, StringComparison.OrdinalIgnoreCase) &&
                    uses.GetValueOrDefault(a.Id) < PlanVariationRules.MaxUsesPerAsset;
                var text = $"{scene.OnScreenText} {scene.Narration}";
                var candidates = catalog.Where(Free).ToList();
                var replacement = BestMatch(candidates, scene.Type, Words(text)) ?? BestMatch(candidates, scene.Type, Words($"{text} {topic}"));
                updated = scene with { AssetIds = replacement is null ? [] : [replacement.Id] };
            }

            var final = updated.AssetIds.FirstOrDefault();
            if (final is not null)
            {
                uses[final] = uses.GetValueOrDefault(final) + 1;
            }

            previous = final;
            result.Add(updated);
        }

        return result;
    }

    private static AssetMetadata? BestMatch(IReadOnlyList<AssetMetadata> catalog, SceneType sceneType, IReadOnlySet<string> words) =>
        catalog
            .Where(a => a.CompatibleSceneTypes.Contains(sceneType))
            .Select(a => (Asset: a, Score: Score(a, words)))
            .Where(c => c.Score > 0)
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.Asset.Id, StringComparer.Ordinal)
            .Select(c => c.Asset)
            .FirstOrDefault();

    // Whether the text names the asset's subject (its first tag).
    private static bool Names(AssetMetadata asset, IReadOnlySet<string> words) =>
        asset.Tags.Count > 0 && asset.Tags[0].Split('-', ' ').Select(Stem).All(words.Contains);

    // A tag counts when all of its meaningful words occur in the text.
    private static int Score(AssetMetadata asset, IReadOnlySet<string> words) =>
        asset.Tags.Count(tag =>
        {
            var tagWords = tag.Split('-', ' ').Select(Stem).Where(w => !GenericWords.Contains(w)).ToList();
            return tagWords.Count > 0 && tagWords.All(words.Contains);
        });

    // Every word, plus each hyphenated pair joined: "pull-ups" gives "pull", "up" and "pullup".
    private static HashSet<string> Words(string text)
    {
        var lower = text.ToLowerInvariant();
        var words = WordPattern().Matches(lower).Select(m => Stem(m.Value)).ToHashSet(StringComparer.Ordinal);
        foreach (Match pair in HyphenatedPattern().Matches(lower))
        {
            words.Add(Stem(pair.Groups[1].Value + pair.Groups[2].Value));
        }

        return words;
    }

    // Just enough stemming for tags: "pull-ups" ~ "pull-up" (the "ups" half -> "up"), "squats" ~
    // "squat". Applied identically to tags and scene text, so "abs" -> "ab" on both sides still
    // matches; "ss" endings ("press") are left alone.
    private static string Stem(string word) =>
        word.Length > 2 && word.EndsWith('s') && !word.EndsWith("ss", StringComparison.Ordinal) ? word[..^1] : word;

    [GeneratedRegex(@"[a-z0-9]+")]
    private static partial Regex WordPattern();

    [GeneratedRegex(@"([a-z0-9]+)-([a-z0-9]+)")]
    private static partial Regex HyphenatedPattern();
}
