namespace ShortsGenerator.Domain;

/// <param name="DurationSeconds">
/// How long the scene is on screen. The LLM proposes whole seconds; after narration
/// (Day 12) it is replaced by the real, frame-aligned audio timing, hence a double.
/// </param>
/// <param name="SpokenDurationSeconds">
/// How much of the scene, from its start, is actual speech (the rest is the pause before
/// the next sentence). Null until narration exists. Captions are spread over this span.
/// </param>
/// <param name="SecondaryText">
/// Second line some templates need (Day 18): the fact that answers a MythVsFact's myth, the
/// "right" side of a Comparison, a Progress label. Null for templates that don't use it.
/// </param>
/// <param name="Items">Up to 3 short bullet points for List/Recovery templates.</param>
/// <param name="Shot">Storyboard videos: the shot that draws this scene (null = the SceneType template).</param>
/// <param name="AltAssetIds">Shots with a second visual: SplitFix/HookQuestion the wrong form; FixDemo/LevelShow the other pose of the pair.</param>
/// <param name="FocusPoint">Where on the figure the point is made, as [x, y] fractions of the main asset's figure bounds.</param>
/// <param name="Arrow">Direction to correct in: up, down, in, out (null = none).</param>
/// <param name="Count">Countdown number or level number.</param>
/// <param name="Total">How many items or levels the video has.</param>
/// <param name="GoalValue">Levels: the benchmark's number (60 for "held for 60 seconds"), counted up on screen.</param>
/// <param name="GoalUnit">"seconds" (a stopwatch) or "reps" (a rep counter).</param>
public sealed record Scene(
    string Id,
    int Order,
    SceneType Type,
    double DurationSeconds,
    string? Narration,
    string? OnScreenText,
    IReadOnlyList<string> AssetIds,
    AnimationId Animation,
    SfxId? Sfx,
    string? Background,
    string? Emphasis,
    double? SpokenDurationSeconds = null,
    string? SecondaryText = null,
    IReadOnlyList<string>? Items = null,
    ShotType? Shot = null,
    IReadOnlyList<string>? AltAssetIds = null,
    IReadOnlyList<double>? FocusPoint = null,
    string? Arrow = null,
    int? Count = null,
    int? Total = null,
    int? GoalValue = null,
    string? GoalUnit = null);
