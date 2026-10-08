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

    // Rejects obviously unsafe/malformed asset ids (e.g. path separators) before even
    // checking whether the id is a real, known asset.
    private static readonly Regex AssetIdPattern = new(@"^[a-z0-9][a-z0-9_-]*$", RegexOptions.Compiled);

    /// <param name="checkDurations">
    /// False for plans already timed from real narration audio (Day 12): their scene lengths are
    /// measured, not guessed, so the duration limits (meant to catch implausible LLM guesses)
    /// don't apply - a short real voice-over is not an invalid plan.
    /// </param>
    public static ValidationResult Validate(
        VideoPlan plan, string expectedNarrationText, IReadOnlySet<string> knownAssetIds, bool checkDurations = true)
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

        if (plan.Scenes.Count < MinScenes || plan.Scenes.Count > MaxScenes)
        {
            reasons.Add($"Plan has {plan.Scenes.Count} scenes; must have between {MinScenes} and {MaxScenes}.");
        }

        ValidateSceneOrdering(plan.Scenes, reasons);
        ValidateIndividualScenes(plan.Scenes, knownAssetIds, checkDurations, reasons);
        if (checkDurations)
        {
            ValidateTotalDuration(plan, reasons);
        }
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

    private static void ValidateIndividualScenes(
        IReadOnlyList<Scene> scenes, IReadOnlySet<string> knownAssetIds, bool checkDurations, List<string> reasons)
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

            if (checkDurations &&
                (scene.DurationSeconds < MinSceneDurationSeconds || scene.DurationSeconds > MaxSceneDurationSeconds))
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
                else if (!knownAssetIds.Contains(assetId))
                {
                    reasons.Add(
                        $"Scene '{scene.Id}' references unknown asset id '{assetId}'. " +
                        "Only asset ids from the provided catalog may be used.");
                }
            }
        }
    }

    // TargetDurationSeconds is derived by the caller from these same scenes (see
    // ScenePlanningService), so the only thing worth checking here is whether the actual
    // runtime lands in the product's allowed video length - not whether two numbers the
    // model might separately report happen to agree with each other.
    private static void ValidateTotalDuration(VideoPlan plan, List<string> reasons)
    {
        var totalDuration = plan.Scenes.Sum(s => s.DurationSeconds);
        if (totalDuration < MinTargetDurationSeconds || totalDuration > MaxTargetDurationSeconds)
        {
            reasons.Add(
                $"Scene durations add up to {totalDuration}s; the whole video must be between " +
                $"{MinTargetDurationSeconds} and {MaxTargetDurationSeconds} seconds.");
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
