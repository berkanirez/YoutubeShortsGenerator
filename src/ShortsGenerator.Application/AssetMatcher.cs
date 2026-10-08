using System.Text.RegularExpressions;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Application;

// A deterministic second opinion on the model's asset choice, driven only by catalog
// metadata (tags + compatible scene types). Found on Day 20: with core-highlight in the
// catalog, the model still put biceps-highlight on a scene titled "Core Muscle". If the
// chosen asset's tags have nothing to do with what the scene says, and another compatible
// asset's tags do, the better match wins. Well-tagged new assets therefore improve
// selection with no code change - the point of the asset system (CLAUDE.md section 10).
public static partial class AssetMatcher
{
    public static IReadOnlyList<string> Improve(
        IReadOnlyList<string> chosen, SceneType sceneType, string sceneText, IReadOnlyList<AssetMetadata> catalog)
    {
        var words = Words(sceneText);
        var current = chosen.Count > 0
            ? catalog.FirstOrDefault(a => string.Equals(a.Id, chosen[0], StringComparison.OrdinalIgnoreCase))
            : null;
        if (current is not null && Score(current, words) > 0)
        {
            return chosen;
        }

        var best = catalog
            .Where(a => a.CompatibleSceneTypes.Contains(sceneType))
            .Select(a => (Asset: a, Score: Score(a, words)))
            .Where(c => c.Score > 0)
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.Asset.Id, StringComparer.Ordinal)
            .FirstOrDefault();

        // No asset at all is a choice the renderer handles (mascot fallback), so a scene
        // without a chosen asset only gets one when something actually matches.
        return best.Asset is null ? chosen : [best.Asset.Id];
    }

    private static int Score(AssetMetadata asset, IReadOnlySet<string> words) =>
        asset.Tags.Count(tag => tag.Split('-', ' ').Select(Stem).All(words.Contains));

    private static HashSet<string> Words(string text) =>
        WordPattern().Matches(text.ToLowerInvariant()).Select(m => Stem(m.Value)).ToHashSet(StringComparer.Ordinal);

    // Just enough stemming for tags: "pull-ups" ~ "pull-up" (the "ups" half -> "up"), "squats" ~
    // "squat". Applied identically to tags and scene text, so "abs" -> "ab" on both sides still
    // matches; "ss" endings ("press") are left alone.
    private static string Stem(string word) =>
        word.Length > 2 && word.EndsWith('s') && !word.EndsWith("ss", StringComparison.Ordinal) ? word[..^1] : word;

    [GeneratedRegex(@"[a-z0-9]+")]
    private static partial Regex WordPattern();
}
