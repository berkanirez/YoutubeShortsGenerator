namespace ShortsGenerator.Domain;

public sealed record VideoPlan(
    string Title,
    string Hook,
    int TargetDurationSeconds,
    string NarrationText,
    IReadOnlyList<Scene> Scenes,
    string? Style,
    string? CaptionStyle,
    string? MusicMood);
