namespace ShortsGenerator.Domain;

// Anti-repetition rules for one video (CLAUDE.md section 4: varied visual combinations,
// no low-value template spam). These are *soft* rules, unlike VideoPlanValidator's: a plan
// that breaks them still renders correctly, it's just less varied. ScenePlanningService
// asks the model to fix them for a couple of attempts, then accepts the plan and records
// the issues as review warnings instead of failing the whole video over them.
public static class PlanVariationRules
{
    public const int MaxScenesPerType = 2;
    public const int MaxUsesPerAsset = 2;
    public const int MinDistinctMiddleTypes = 3;

    public static IReadOnlyList<string> Check(VideoPlan plan)
    {
        var reasons = new List<string>();
        var scenes = plan.Scenes.OrderBy(s => s.Order).ToList();
        // Hook and Outro are fixed by structure; variety is about what's in between.
        var middle = scenes.Where(s => s.Type is not (SceneType.Hook or SceneType.Outro)).ToList();

        foreach (var group in middle.GroupBy(s => s.Type).Where(g => g.Count() > MaxScenesPerType))
        {
            reasons.Add(
                $"Scene type {group.Key} is used {group.Count()} times; use each type at most {MaxScenesPerType} times.");
        }

        var distinctTypes = middle.Select(s => s.Type).Distinct().Count();
        if (middle.Count >= 4 && distinctTypes < MinDistinctMiddleTypes)
        {
            reasons.Add(
                $"Only {distinctTypes} different scene types are used between Hook and Outro; use at least {MinDistinctMiddleTypes}.");
        }

        for (var i = 1; i < middle.Count; i++)
        {
            if (middle[i].Type == middle[i - 1].Type && middle[i].Order == middle[i - 1].Order + 1)
            {
                reasons.Add($"Scenes {middle[i - 1].Order} and {middle[i].Order} are both {middle[i].Type}; vary consecutive scenes.");
            }
        }

        var assetUses = scenes.SelectMany(s => s.AssetIds.Take(1)).GroupBy(id => id, StringComparer.OrdinalIgnoreCase);
        foreach (var group in assetUses.Where(g => g.Count() > MaxUsesPerAsset))
        {
            reasons.Add($"Asset '{group.Key}' is shown in {group.Count()} scenes; show each asset at most {MaxUsesPerAsset} times.");
        }

        for (var i = 1; i < scenes.Count; i++)
        {
            var previous = scenes[i - 1].AssetIds.FirstOrDefault();
            var current = scenes[i].AssetIds.FirstOrDefault();
            if (current is not null && string.Equals(previous, current, StringComparison.OrdinalIgnoreCase))
            {
                reasons.Add($"Scenes {scenes[i - 1].Order} and {scenes[i].Order} both show '{current}'; pick a different asset for one of them.");
            }
        }

        return reasons;
    }

    public const int OpeningLength = 3;

    // Variety *across* videos. Found on Day 19 by comparing five real plans: each one was
    // varied inside, but scene 3 was AnatomyHighlight in all five and scene 2 MythVsFact in
    // four - the small model copies the order its prompt lists the templates in. Back to
    // back on a channel, that's the same skeleton every time.
    public static IReadOnlyList<string> CheckAgainstEarlier(
        VideoPlan plan, IReadOnlyList<(string Topic, VideoPlan Plan)> earlier)
    {
        var opening = Opening(plan);
        if (opening.Count < OpeningLength)
        {
            return [];
        }

        var match = earlier.FirstOrDefault(e => Opening(e.Plan).SequenceEqual(opening));
        return match.Plan is null
            ? []
            : [$"Scenes 2-{OpeningLength + 1} use the same types in the same order ({string.Join(", ", opening)}) as an " +
               $"earlier video (\"{match.Topic}\"); start this one differently."];
    }

    private static IReadOnlyList<SceneType> Opening(VideoPlan plan) =>
        plan.Scenes
            .OrderBy(s => s.Order)
            .Where(s => s.Type is not (SceneType.Hook or SceneType.Outro))
            .Take(OpeningLength)
            .Select(s => s.Type)
            .ToList();
}
