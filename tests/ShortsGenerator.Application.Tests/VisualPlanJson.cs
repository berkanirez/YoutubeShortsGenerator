using System.Text.Json;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Application.Tests;

// Builds what a well-behaved LLM returns for scene planning: one visual entry per scene the
// narration is split into - the same split ScenePlanningService does itself. By default the
// middle scene types rotate and a takeaway is given, so the plan passes the Day 19
// variety rules; tests that are about those rules override typeFor / takeaway.
internal static class VisualPlanJson
{
    private static readonly string[] MiddleTypes =
        ["ExerciseDemo", "MythVsFact", "Progress", "List", "Comparison", "AnatomyHighlight", "Recovery"];

    public static string For(
        string narration,
        IReadOnlyList<string>? firstSceneAssetIds = null,
        int? sceneCountOverride = null,
        Func<int, string?>? emphasisFor = null,
        Func<int, string>? animationFor = null,
        Func<int, string>? onScreenTextFor = null,
        Func<int, string?>? sfxFor = null,
        string? musicMood = "upbeat",
        Func<int, string?>? secondaryTextFor = null,
        Func<int, string[]>? itemsFor = null,
        Func<int, string>? typeFor = null,
        Func<int, string[]>? assetsFor = null,
        string? takeaway = "Strict reps beat sloppy volume for pull-up progress")
    {
        var count = sceneCountOverride ??
            NarrationSegmenter.Split(narration, VideoPlanValidator.MinScenes, VideoPlanValidator.MaxScenes).Count;

        var scenes = Enumerable.Range(0, count).Select(i => new Dictionary<string, object?>
        {
            ["scene"] = i + 1,
            ["type"] = typeFor?.Invoke(i) ?? (i == 0 ? "Hook" : i == count - 1 ? "Outro" : MiddleTypes[(i - 1) % MiddleTypes.Length]),
            ["onScreenText"] = onScreenTextFor?.Invoke(i) ?? $"Scene {(char)('A' + i)}",   // no digits: a number on screen must be one the narration says (Day 29)
            ["assetIds"] = assetsFor?.Invoke(i) ?? (i == 0 ? firstSceneAssetIds ?? [] : Array.Empty<string>()),
            ["animation"] = animationFor?.Invoke(i) ?? "fade",
            ["sfx"] = sfxFor?.Invoke(i),
            ["emphasis"] = emphasisFor?.Invoke(i),
            ["secondaryText"] = secondaryTextFor?.Invoke(i),
            ["items"] = itemsFor?.Invoke(i) ?? [],
        });

        return JsonSerializer.Serialize(new { title = "Pull-ups", hook = "Stuck?", takeaway, musicMood, scenes });
    }
}
