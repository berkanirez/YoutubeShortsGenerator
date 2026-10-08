namespace ShortsGenerator.Domain;

// The caption presets the renderer implements (renderer/src/captions/captionStyles.ts).
// A plan may only name one of these - never raw styling.
public static class CaptionStyleId
{
    public const string Bold = "bold";
    public const string Boxed = "boxed";

    public static IReadOnlyList<string> All { get; } = [Bold, Boxed];

    public static bool IsKnown(string? id) => All.Contains(id, StringComparer.OrdinalIgnoreCase);
}
