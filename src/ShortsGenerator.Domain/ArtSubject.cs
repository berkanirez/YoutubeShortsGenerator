namespace ShortsGenerator.Domain;

public enum ArtKind
{
    // A full-body mascot with one muscle group painted red (forearms-highlight).
    Muscle,

    // Two poses side by side in one image, split into <id>-top / <id>-bottom (kettlebell-swing).
    ExercisePair,

    // One pose (plank, wall-sit).
    Exercise,

    // A piece of gym equipment, no character (kettlebell).
    Equipment,

    // A simple object/symbol, no character (water bottle).
    Icon,

    // Another expression of the mascot itself (mascot-facepalm). Never asked for by a scene -
    // VisualDirector rotates through whichever expressions exist - but listed and imported
    // like the rest.
    MascotPose,

    // An exercise done WRONG on purpose (pushup-hips-sag), for the wrong-vs-right split. Never
    // matched from narration words; a storyboard asks for it by id.
    Mistake,

    // Four animation frames of one movement (a 2x2 sprite sheet): <stem>-f1..f4, played as a
    // flipbook - the rep of an exercise (stem = Exercise) or a mascot gesture (stem = Id).
    Cycle,
}

// One thing a scene can need a picture of. Loaded from assets/manifests/subjects.json, so a
// new subject is a JSON edit, not a code change. The catalog asset ids, the file the owner
// saves the generated image as, and the image prompt are all derived from it - the LLM only
// ever *mentions* a subject in the narration; it never names files or writes prompts.
/// <param name="Id">kebab-case, also the asset id stem: "forearms", "kettlebell-swing".</param>
/// <param name="Terms">Words that name it in a narration ("forearm", "forearms", "grip").</param>
/// <param name="Describe">What the picture shows; goes into the prompt.</param>
/// <param name="Top">ExercisePair: the LEFT pose (top / start of the rep).</param>
/// <param name="Bottom">ExercisePair: the RIGHT pose (bottom / end of the rep).</param>
/// <param name="Camera">ExercisePair: camera and what stays fixed ("side view, feet at the same spot").</param>
/// <param name="BackView">Muscle: the muscle is seen from behind.</param>
/// <param name="Hanging">The figure hangs from a bar (pull-up): placed by the bar, not standing on the floor.</param>
/// <param name="Exercise">Mistake: the exercise subject id it belongs to.</param>
/// <param name="Pose">Mistake: which picture of the exercise shows the right way (top / bottom / single).</param>
/// <param name="Label">Mistake: its name on screen, 1-3 words ("SAGGING HIPS").</param>
/// <param name="FixLabel">Mistake: the fix on screen, 1-3 words ("SQUEEZE GLUTES").</param>
/// <param name="Point">Mistake: where on the body, [x, y] fractions of the right-way picture's figure bounds.</param>
/// <param name="Arrow">Mistake: the direction that fixes it (up, down, in, out) or null.</param>
public sealed record ArtSubject(
    string Id,
    ArtKind Kind,
    IReadOnlyList<string> Terms,
    string Describe,
    string? Top = null,
    string? Bottom = null,
    string? Camera = null,
    bool BackView = false,
    bool Hanging = false,
    string? Exercise = null,
    string? Pose = null,
    string? Label = null,
    string? FixLabel = null,
    IReadOnlyList<double>? Point = null,
    string? Arrow = null)
{
    public IReadOnlyList<string> AssetIds => Kind switch
    {
        ArtKind.Muscle => [$"{Id}-highlight"],
        ArtKind.ExercisePair => [$"{Id}-top", $"{Id}-bottom"],
        ArtKind.Cycle => [.. Enumerable.Range(1, 4).Select(n => $"{CycleStem}-f{n}")],
        _ => [Id],
    };

    // pushup-cycle -> "pushup" (its exercise); mascot-point -> "mascot-point".
    public string CycleStem => Exercise ?? Id;

    // Saved by the owner into assets/incoming/ (or uploaded through the dashboard).
    public string IncomingFileName => Kind switch
    {
        ArtKind.Muscle => $"{Id}-highlight.png",
        ArtKind.ExercisePair => $"{Id}-pair.png",
        ArtKind.Cycle => Exercise is null ? $"{Id}-anim.png" : $"{Id}.png",
        _ => $"{Id}.png",
    };

    // The catalog category (assets/<folder>/ is chosen from it).
    public string Category => Kind switch
    {
        ArtKind.Muscle => "anatomy",
        ArtKind.ExercisePair or ArtKind.Exercise or ArtKind.Mistake => "exercise",
        ArtKind.Cycle => Exercise is null ? "mascot" : "exercise",
        ArtKind.Equipment => "equipment",
        ArtKind.MascotPose => "mascot",
        _ => "icon",
    };

    public IReadOnlyList<SceneType> SceneTypes => Kind switch
    {
        ArtKind.Muscle => [SceneType.AnatomyHighlight],
        ArtKind.ExercisePair => [SceneType.ExerciseDemo, SceneType.AnatomyHighlight, SceneType.Comparison],
        ArtKind.Exercise => [SceneType.ExerciseDemo, SceneType.Comparison, SceneType.List],
        ArtKind.Equipment => [SceneType.ExerciseDemo, SceneType.List, SceneType.Progress],
        ArtKind.Mistake => [SceneType.Comparison, SceneType.MythVsFact],
        ArtKind.Cycle => [SceneType.ExerciseDemo],
        ArtKind.MascotPose => [SceneType.Hook, SceneType.Outro, SceneType.MythVsFact, SceneType.Comparison, SceneType.List, SceneType.Progress, SceneType.Recovery],
        _ => [SceneType.List, SceneType.Recovery, SceneType.MythVsFact, SceneType.Progress],
    };

    // Where the renderer pins the picture: feet on the floor, the bar at the top, or centered.
    public string Anchor => Kind switch
    {
        ArtKind.Equipment or ArtKind.Icon => "center",
        _ when Hanging => "top",
        _ => "bottom",
    };

    // Marks art the classic planner must never pick on its own: a wrong-form drawing (only the
    // storyboard's split shows it - a fat-loss hook once showed caving knees) and the single
    // frames of an animation (the renderer plays them together).
    public const string WrongFormTag = "wrong-form";

    // First tag = the subject (docs/ASSETS.md), then its single-word / kebab terms.
    public IReadOnlyList<string> Tags =>
        new[] { Id }.Concat(Kind is ArtKind.Mistake or ArtKind.Cycle ? [WrongFormTag] : []).Concat(Terms.Select(t => t.Trim().ToLowerInvariant().Replace(' ', '-')))
            .Distinct()
            .Take(8)
            .ToList();
}

public interface IArtSubjectCatalog
{
    Task<IReadOnlyList<ArtSubject>> GetAllAsync(CancellationToken cancellationToken);
}
