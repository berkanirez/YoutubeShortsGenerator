namespace ShortsGenerator.Domain;

// The image prompt for a missing picture, assembled from fixed blocks (the same wording as
// docs/ART_PROMPTS_READY.md, which produced the current art) plus the subject's own
// description from subjects.json. Nothing here comes from the LLM.
public static class ArtPromptBuilder
{
    public const string Character =
        "Use this exact character from the reference image: an athletic male fitness mascot, clean anime " +
        "cel-shaded style, bold black outlines, spiky messy red hair, simple white eyes with no pupils, warm " +
        "natural tan skin, muscular, loose bright red shorts, grey compression sleeve on his LEFT forearm, grey " +
        "compression sleeve on his RIGHT knee, barefoot.";

    public static bool NeedsReferenceImage(ArtSubject subject) =>
        subject.Kind is ArtKind.Muscle or ArtKind.ExercisePair or ArtKind.Exercise or ArtKind.MascotPose;

    public static string Build(ArtSubject subject) => subject.Kind switch
    {
        ArtKind.ExercisePair =>
            $"{Character}\n" +
            "Make one wide landscape image (2:1) showing the SAME character TWICE side by side, same size, " +
            $"{subject.Camera ?? "same side-view camera, feet at exactly the same position in both halves"}.\n" +
            $"LEFT half: {subject.Top}.\n" +
            $"RIGHT half: {subject.Bottom}.\n" +
            "Pure flat white background, no floor line, no shadows, no text, no letters, no arrows, no icons. " +
            "Full body visible in both halves, nothing cut off.",
        ArtKind.Exercise or ArtKind.MascotPose =>
            $"{Character}\n" +
            $"Pose: {subject.Describe}.\n" +
            "Square image. Pure flat white background, no floor line, no shadows, no text, no letters, no arrows, " +
            "no icons. Full body visible, nothing cut off.",
        ArtKind.Muscle =>
            $"{Character}\n" +
            (subject.BackView
                ? "Pose: standing relaxed, BACK view (seen from behind), arms slightly away from the body.\n"
                : "Pose: standing relaxed, front view, arms slightly away from the body.\n") +
            $"The {subject.Describe} are colored solid bright red (#EF4444) like an anatomy highlight; " +
            "everything else in his normal colors.\n" +
            "Square image. Pure flat white background, no shadows, no text, no letters, no arrows, no icons. " +
            "Full body visible, nothing cut off.",
        _ =>
            $"{Capitalize(subject.Describe)}, as a simple icon in a clean anime cel-shaded style with bold black " +
            "outlines. Centered, square image, pure flat white background, no shadow, no text, no letters, no numbers.",
    };

    private static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
