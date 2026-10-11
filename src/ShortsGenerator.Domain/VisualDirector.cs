namespace ShortsGenerator.Domain;

// Keeps the channel from looking mass-produced: the choices the viewer sees in every video
// (which mascot expression, how the hook moves, which look/transition set) are made here, in
// code, against the last few finished videos - the least recently used option wins, so the
// same expression comes back about every fifth video instead of every time. Deterministic
// for one job (seed), so a re-run gives the same plan.
public static class VisualDirector
{
    public const int RecentVideos = 5;

    // Look sets the renderer knows (hook layout x transition style); see renderer variant.ts.
    public const int VariantCount = 6;

    // Expressions that fit each kind of beat, in no particular order.
    private static readonly IReadOnlyDictionary<SceneType, string[]> PosesBySceneType = new Dictionary<SceneType, string[]>
    {
    // Only the ones in the catalog are used, so expressions listed here before their picture
    // exists (subjects.json, kind MascotPose) join the rotation as soon as they're imported.
        [SceneType.Hook] = ["mascot-thinking", "mascot-shocked", "mascot-pointing", "mascot-stop", "mascot-flex", "mascot-confused", "mascot-mind-blown", "mascot-crossed-arms", "mascot-idea", "mascot-facepalm"],
        [SceneType.Outro] = ["mascot-happy", "mascot-thumbsup", "mascot-flex", "mascot-pointing", "mascot-wave", "mascot-celebrate", "mascot-crossed-arms"],
        [SceneType.MythVsFact] = ["mascot-stop", "mascot-thinking", "mascot-shocked", "mascot-facepalm", "mascot-confused", "mascot-laughing"],
        [SceneType.Comparison] = ["mascot-thinking", "mascot-stop", "mascot-pointing", "mascot-shrug", "mascot-confused"],
        [SceneType.List] = ["mascot-pointing", "mascot-idle", "mascot-thumbsup", "mascot-idea", "mascot-writing"],
        [SceneType.Progress] = ["mascot-flex", "mascot-happy", "mascot-thumbsup", "mascot-celebrate", "mascot-writing"],
        [SceneType.Recovery] = ["mascot-sleeping", "mascot-tired", "mascot-idle", "mascot-exhausted", "mascot-drinking", "mascot-stretching", "mascot-sitting"],
        [SceneType.ExerciseDemo] = ["mascot-idle", "mascot-flex"],
        [SceneType.AnatomyHighlight] = ["mascot-flex", "mascot-idle"],
    };

    private static readonly AnimationId[] HookAnimations =
        [AnimationId.PunchZoom, AnimationId.Pop, AnimationId.Shake, AnimationId.Bounce, AnimationId.Slide];

    /// <param name="earlier">Finished videos' plans, most recent first.</param>
    public static VideoPlan Direct(VideoPlan plan, IReadOnlyList<VideoPlan> earlier, IReadOnlyCollection<string> catalogIds, int seed)
    {
        var available = new HashSet<string>(catalogIds, StringComparer.OrdinalIgnoreCase);
        var recent = earlier.Take(RecentVideos).ToList();

        // How strongly an earlier use counts: 5 for the last video ... 1 for the fifth, times the
        // weight usedIn gives it (0 = not used).
        int Recency(Func<VideoPlan, int> usedIn) => recent.Select((p, k) => usedIn(p) * (RecentVideos - k)).Sum();

        T LeastRecent<T>(IReadOnlyList<T> options, Func<T, int> extra, Func<VideoPlan, T, int> usedIn) =>
            options
                .Select((option, i) => (option, score: Recency(p => usedIn(p, option)) + extra(option), tie: (i + seed) % options.Count))
                .OrderBy(c => c.score)
                .ThenBy(c => c.tie)
                .First()
                .option;

        var scenes = new List<Scene>();
        var usedHere = new List<string>();
        foreach (var original in plan.Scenes.OrderBy(s => s.Order))
        {
            var scene = original;
            var current = scene.AssetIds.FirstOrDefault();
            var mascotSlot = current is null || current.StartsWith("mascot-", StringComparison.OrdinalIgnoreCase);
            if (mascotSlot && PosesBySceneType.TryGetValue(scene.Type, out var pool))
            {
                var candidates = pool.Where(available.Contains).ToList();
                if (candidates.Count > 0)
                {
                    var previous = scenes.LastOrDefault()?.AssetIds.FirstOrDefault();
                    var pose = LeastRecent(
                        candidates,
                        p => (usedHere.Contains(p) ? 100 : 0) + (string.Equals(p, previous, StringComparison.OrdinalIgnoreCase) ? 1000 : 0),
                        (earlierPlan, p) => PoseWeight(earlierPlan, p, scene.Type));
                    scene = scene with { AssetIds = [pose] };
                }
            }

            if (scene.Type == SceneType.Hook)
            {
                var animation = LeastRecent(
                    HookAnimations,
                    _ => 0,
                    (earlierPlan, a) => earlierPlan.Scenes.Any(s => s.Type == SceneType.Hook && s.Animation == a) ? 10 : 0);
                scene = scene with { Animation = animation };
            }

            usedHere.Add(scene.AssetIds.FirstOrDefault() ?? string.Empty);
            scenes.Add(scene);
        }

        var variant = LeastRecent(Enumerable.Range(0, VariantCount).ToList(), _ => 0, (earlierPlan, v) => earlierPlan.Variant == v ? 10 : 0);
        return plan with { Scenes = scenes, Variant = variant };
    }

    // An expression seen in the same kind of beat (the last hook, the last outro) is what the
    // viewer remembers most: full weight. Seen elsewhere in the video: a little.
    private static int PoseWeight(VideoPlan earlierPlan, string pose, SceneType type)
    {
        var uses = earlierPlan.Scenes.Where(s => string.Equals(s.AssetIds.FirstOrDefault(), pose, StringComparison.OrdinalIgnoreCase)).ToList();
        return uses.Any(s => s.Type == type) ? 10 : uses.Count > 0 ? 3 : 0;
    }
}
