using System.Text.RegularExpressions;

namespace ShortsGenerator.Domain;

public static class VideoPlanValidator
{
    public const int MinScenes = 6;
    public const int MaxScenes = 10;
    public const int MinSceneDurationSeconds = 1;
    public const int MaxSceneDurationSeconds = 12;
    public const int MinTargetDurationSeconds = 20;
    public const int MaxTargetDurationSeconds = 50;

    // How far the sum of scene durations may drift from TargetDurationSeconds.
    private const int DurationToleranceSeconds = 5;

    // Asset IDs don't exist yet (the manifest arrives Day 6) - this only rejects
    // obviously unsafe/malformed values (e.g. path separators) this early.
    private static readonly Regex AssetIdPattern = new(@"^[a-z0-9][a-z0-9_-]*$", RegexOptions.Compiled);

    public static ValidationResult Validate(VideoPlan plan, string expectedNarrationText)
    {
        var reasons = new List<string>();

        if (string.IsNullOrWhiteSpace(plan.Title))
        {
            reasons.Add("Title is empty.");
        }

        if (string.IsNullOrWhiteSpace(plan.Hook))
        {
            reasons.Add("Hook is empty.");
        }

        if (plan.TargetDurationSeconds < MinTargetDurationSeconds || plan.TargetDurationSeconds > MaxTargetDurationSeconds)
        {
            reasons.Add(
                $"TargetDurationSeconds ({plan.TargetDurationSeconds}) must be between " +
                $"{MinTargetDurationSeconds} and {MaxTargetDurationSeconds}.");
        }

        if (plan.Scenes.Count < MinScenes || plan.Scenes.Count > MaxScenes)
        {
            reasons.Add($"Plan has {plan.Scenes.Count} scenes; must have between {MinScenes} and {MaxScenes}.");
        }

        ValidateSceneOrdering(plan.Scenes, reasons);
        ValidateIndividualScenes(plan.Scenes, reasons);
        ValidateTotalDuration(plan, reasons);
        ValidateNarrationReconstruction(plan.Scenes, expectedNarrationText, reasons);

        return new ValidationResult(reasons.Count == 0, reasons);
    }

    private static void ValidateSceneOrdering(IReadOnlyList<Scene> scenes, List<string> reasons)
    {
        if (scenes.Count == 0)
        {
            return;
        }

        var expectedOrders = Enumerable.Range(1, scenes.Count).ToHashSet();
        var actualOrders = scenes.Select(s => s.Order).ToHashSet();
        if (!expectedOrders.SetEquals(actualOrders))
        {
            reasons.Add($"Scene 'order' values must be exactly 1..{scenes.Count}, each used once.");
            return;
        }

        var ordered = scenes.OrderBy(s => s.Order).ToList();
        if (ordered[0].Type != SceneType.Hook)
        {
            reasons.Add("The first scene (order 1) must be of type 'Hook'.");
        }

        if (ordered[^1].Type != SceneType.Outro)
        {
            reasons.Add("The last scene must be of type 'Outro'.");
        }

        var duplicateIds = scenes.GroupBy(s => s.Id).Where(g => g.Count() > 1).Select(g => g.Key);
        foreach (var id in duplicateIds)
        {
            reasons.Add($"Scene id '{id}' is used more than once; every scene needs a unique id.");
        }
    }

    private static void ValidateIndividualScenes(IReadOnlyList<Scene> scenes, List<string> reasons)
    {
        foreach (var scene in scenes)
        {
            if (string.IsNullOrWhiteSpace(scene.Id))
            {
                reasons.Add($"Scene at order {scene.Order} has an empty id.");
            }

            // Outro scenes may be a silent, visual-only closing card (CLAUDE.md's
            // OutroCard) when the narration has no leftover closing line - every other
            // scene type must carry real narration.
            if (scene.Type != SceneType.Outro && string.IsNullOrWhiteSpace(scene.Narration))
            {
                reasons.Add($"Scene '{scene.Id}' (order {scene.Order}) has empty narration.");
            }

            if (scene.DurationSeconds < MinSceneDurationSeconds || scene.DurationSeconds > MaxSceneDurationSeconds)
            {
                reasons.Add(
                    $"Scene '{scene.Id}' duration ({scene.DurationSeconds}s) must be between " +
                    $"{MinSceneDurationSeconds} and {MaxSceneDurationSeconds} seconds.");
            }

            foreach (var assetId in scene.AssetIds)
            {
                if (!AssetIdPattern.IsMatch(assetId))
                {
                    reasons.Add(
                        $"Scene '{scene.Id}' has an invalid asset id '{assetId}' " +
                        "(use lowercase letters, digits, '-', '_' only).");
                }
            }
        }
    }

    private static void ValidateTotalDuration(VideoPlan plan, List<string> reasons)
    {
        var totalDuration = plan.Scenes.Sum(s => s.DurationSeconds);
        if (Math.Abs(totalDuration - plan.TargetDurationSeconds) > DurationToleranceSeconds)
        {
            reasons.Add(
                $"Scene durations add up to {totalDuration}s, which is too far from " +
                $"TargetDurationSeconds ({plan.TargetDurationSeconds}s).");
        }
    }

    private static void ValidateNarrationReconstruction(
        IReadOnlyList<Scene> scenes, string expectedNarrationText, List<string> reasons)
    {
        var reconstructed = NormalizeWhitespace(string.Join(' ', scenes.OrderBy(s => s.Order).Select(s => s.Narration)));
        var expected = NormalizeWhitespace(expectedNarrationText);

        if (!string.Equals(reconstructed, expected, StringComparison.Ordinal))
        {
            reasons.Add(
                "Concatenating all scene narrations in order does not exactly reproduce the " +
                "original narration text - do not paraphrase, only split it.");
        }
    }

    private static string NormalizeWhitespace(string text) => Regex.Replace(text.Trim(), @"\s+", " ");
}
