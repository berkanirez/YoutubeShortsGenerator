namespace ShortsGenerator.Domain;

public sealed record VideoPlan(
    string Title,
    string Hook,
    double TargetDurationSeconds,
    string NarrationText,
    IReadOnlyList<Scene> Scenes,
    string? Style,
    string? CaptionStyle,
    string? MusicMood,
    // Day 19: the one lesson the viewer should leave with - checked to be different from
    // earlier videos' takeaways.
    string? Takeaway = null,
    // Day 19: soft-rule issues (variation, near-duplicate takeaway) the plan was accepted
    // with; surfaced in the job's review checklist for the human reviewer.
    IReadOnlyList<string>? ReviewWarnings = null,
    // Day 23: a scene's narration text was edited, so narration.wav and every scene's timing
    // no longer match the words. Rendering is refused until the narration is regenerated.
    bool NarrationOutdated = false,
    // The scenes were checked against the art catalog (ArtGapDetector) and every needed
    // picture exists - or the owner chose to continue without the missing ones.
    bool ArtChecked = false,
    // Which look set the renderer uses (hook layout, transitions, motion style), picked by
    // VisualDirector so consecutive videos don't look alike. Null = derived from the text.
    int? Variant = null);
