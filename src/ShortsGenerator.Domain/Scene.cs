namespace ShortsGenerator.Domain;

public sealed record Scene(
    string Id,
    int Order,
    SceneType Type,
    int DurationSeconds,
    string? Narration,
    string? OnScreenText,
    IReadOnlyList<string> AssetIds,
    AnimationId Animation,
    SfxId? Sfx,
    string? Background,
    string? Emphasis);
