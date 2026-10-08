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
    IReadOnlyList<string>? Items = null);
