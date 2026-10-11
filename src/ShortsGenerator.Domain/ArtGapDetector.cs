using System.Text.RegularExpressions;

namespace ShortsGenerator.Domain;

/// <summary>A picture the catalog doesn't have yet, and the scenes that need it.</summary>
public sealed record ArtRequest(ArtSubject Subject, IReadOnlyList<string> SceneIds, string Prompt);

/// <param name="Scenes">The plan's scenes, each pointing at its subject's art where the catalog has it.</param>
/// <param name="Requests">Subjects the plan needs that have no art yet, one entry per subject.</param>
public sealed record ArtCheck(IReadOnlyList<Scene> Scenes, IReadOnlyList<ArtRequest> Requests);

// Decides, in code, what each scene must SHOW: the subject its own words name. A scene about
// forearms shows the forearm picture - or, when there is none, the video asks for one - but
// never the closest thing the catalog happens to have (biceps). Deterministic, so it can run
// again after new art arrives and give the same answer.
public static partial class ArtGapDetector
{
    // Which kinds of picture fit which scene type. Hook/Outro stay on the mascot.
    private static readonly IReadOnlyDictionary<SceneType, ArtKind[]> KindsBySceneType = new Dictionary<SceneType, ArtKind[]>
    {
        [SceneType.AnatomyHighlight] = [ArtKind.Muscle],
        [SceneType.ExerciseDemo] = [ArtKind.ExercisePair, ArtKind.Exercise],
        [SceneType.Comparison] = [ArtKind.ExercisePair, ArtKind.Exercise, ArtKind.Equipment, ArtKind.Icon],
        [SceneType.MythVsFact] = [ArtKind.ExercisePair, ArtKind.Exercise, ArtKind.Equipment, ArtKind.Icon],
        [SceneType.List] = [ArtKind.Icon, ArtKind.Equipment, ArtKind.ExercisePair, ArtKind.Exercise],
        [SceneType.Recovery] = [ArtKind.Icon, ArtKind.Equipment, ArtKind.ExercisePair, ArtKind.Exercise],
        [SceneType.Progress] = [ArtKind.Icon, ArtKind.Equipment, ArtKind.ExercisePair, ArtKind.Exercise],
    };

    // One subject's picture in at most this many scenes: a squat video showed the squat
    // drawing in 4 of 8 scenes - the rest stay with the mascot, whose expressions vary.
    public const int MaxScenesPerSubject = 2;

    // Scenes that are *about* the subject get its picture first.
    private static bool IsPrimary(SceneType type) => type is SceneType.ExerciseDemo or SceneType.AnatomyHighlight;

    // Topics about the body or lifestyle (fat loss, sleep, nutrition): any middle scene may show
    // an icon, equipment or a single exercise its words name.
    private static readonly ArtKind[] LifestyleKinds = [ArtKind.Icon, ArtKind.Equipment, ArtKind.Exercise, ArtKind.Muscle];

    /// <param name="lifestyle">
    /// The topic is not about exercise technique (TopicBriefs). Then a scene's picture must come
    /// from its own words; an exercise drawing its words don't name is removed (the mascot shows) -
    /// a fat-loss video once showed squats and bench presses behind "calorie deficit".
    /// </param>
    public static ArtCheck Check(IReadOnlyList<Scene> scenes, IReadOnlyCollection<string> catalogIds, IReadOnlyList<ArtSubject> subjects, bool lifestyle = false)
    {
        if (lifestyle)
        {
            scenes = scenes.Select(s => DropUnnamedExercise(s, subjects)).ToList();
        }

        var inCatalog = new HashSet<string>(catalogIds, StringComparer.OrdinalIgnoreCase);
        var updated = new List<Scene>(scenes.Count);
        var missing = new Dictionary<string, (ArtSubject Subject, List<string> SceneIds)>();
        var shown = new Dictionary<string, int>();

        foreach (var scene in scenes.OrderByDescending(s => IsPrimary(s.Type)).ThenBy(s => s.Order))
        {
            // Storyboard scenes choose their own pictures (StoryboardComposer) and name what they
            // lack in the plan's WantedArt; matching their words would undo that.
            var subject = scene.Shot is null ? FindSubject(scene, subjects, lifestyle) : null;
            if (subject is null)
            {
                updated.Add(scene);
                continue;
            }

            if (shown.GetValueOrDefault(subject.Id) >= MaxScenesPerSubject)
            {
                // Enough of this picture: a scene still showing it goes back to the mascot.
                var showsIt = scene.AssetIds.Count > 0 && subject.AssetIds.Contains(scene.AssetIds[0], StringComparer.OrdinalIgnoreCase);
                updated.Add(showsIt ? scene with { AssetIds = [] } : scene);
                continue;
            }

            shown[subject.Id] = shown.GetValueOrDefault(subject.Id) + 1;

            if (subject.AssetIds.All(inCatalog.Contains))
            {
                // Keep the plan's own pick when it already is this subject's art (e.g. the
                // bottom pose of a pair); otherwise point the scene at the subject.
                var keep = scene.AssetIds.Count > 0 && subject.AssetIds.Contains(scene.AssetIds[0], StringComparer.OrdinalIgnoreCase);
                updated.Add(keep ? scene : scene with { AssetIds = [subject.AssetIds[0]] });
                continue;
            }

            if (!missing.TryGetValue(subject.Id, out var entry))
            {
                entry = (subject, []);
                missing[subject.Id] = entry;
            }

            entry.SceneIds.Add(scene.Id);
            updated.Add(scene);
        }

        updated = updated.OrderBy(s => s.Order).ToList();
        var requests = missing.Values
            .Select(m => new ArtRequest(m.Subject, m.SceneIds, ArtPromptBuilder.Build(m.Subject)))
            .ToList();
        return new ArtCheck(updated, requests);
    }

    /// <summary>
    /// The subject a scene is about: the earliest term match in its most visible text (the
    /// on-screen slogan, then the emphasis, then the narration), restricted to the kinds of
    /// picture that fit the scene type. At the same position the longer term wins
    /// ("pull-up bar" over "pull-up").
    /// </summary>
    private static Scene DropUnnamedExercise(Scene scene, IReadOnlyList<ArtSubject> subjects)
    {
        var current = scene.AssetIds.FirstOrDefault();
        var owner = current is null ? null : subjects.FirstOrDefault(s => s.AssetIds.Contains(current, StringComparer.OrdinalIgnoreCase));
        if (owner is null || owner.Kind is not (ArtKind.ExercisePair or ArtKind.Exercise or ArtKind.Mistake))
        {
            return scene;
        }

        var text = Normalize($"{scene.OnScreenText} {scene.Emphasis} {scene.Narration}");
        return owner.Terms.Any(t => IndexOfWord(text, Normalize(t)) >= 0) ? scene : scene with { AssetIds = [] };
    }

    public static ArtSubject? FindSubject(Scene scene, IReadOnlyList<ArtSubject> subjects, bool lifestyle = false)
    {
        ArtKind[]? kinds = lifestyle && scene.Type is not (SceneType.Hook or SceneType.Outro)
            ? LifestyleKinds
            : KindsBySceneType.GetValueOrDefault(scene.Type);
        if (kinds is null)
        {
            return null;
        }

        var candidates = subjects.Where(s => kinds.Contains(s.Kind)).ToList();
        foreach (var text in new[] { scene.OnScreenText, scene.Emphasis, scene.Narration })
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var normalized = Normalize(text);
            var best = candidates
                .SelectMany(subject => subject.Terms.Select(term => (subject, term: Normalize(term))))
                .Where(c => c.term.Length > 0)
                .Select(c => (c.subject, c.term.Length, Position: IndexOfWord(normalized, c.term)))
                .Where(c => c.Position >= 0)
                .OrderBy(c => c.Position)
                .ThenByDescending(c => c.Length)
                .Select(c => c.subject)
                .FirstOrDefault();
            if (best is not null)
            {
                return best;
            }
        }

        return null;
    }

    // "Pull-Ups!" -> "pull ups": hyphens and punctuation become spaces, so a term matches
    // however the narration hyphenates it.
    private static string Normalize(string text) =>
        Spaces().Replace(NonWord().Replace(text.ToLowerInvariant().Replace('’', '\''), " "), " ").Trim();

    private static int IndexOfWord(string text, string term)
    {
        var start = 0;
        while (true)
        {
            var index = text.IndexOf(term, start, StringComparison.Ordinal);
            if (index < 0)
            {
                return -1;
            }

            var before = index == 0 || text[index - 1] == ' ';
            var end = index + term.Length;
            var after = end == text.Length || text[end] == ' ';
            if (before && after)
            {
                return index;
            }

            start = index + 1;
        }
    }

    [GeneratedRegex(@"[^a-z0-9']+")]
    private static partial Regex NonWord();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
