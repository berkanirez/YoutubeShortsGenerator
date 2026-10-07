namespace ShortsGenerator.Domain;

// The controlled animation primitives from CLAUDE.md section 11. Serialized as camelCase
// ("punchZoom") to match CLAUDE.md's own wording, even though the renderer that actually
// implements these (Remotion) doesn't exist until Day 7-9.
[JsonCamelCaseEnumConverter]
public enum AnimationId
{
    Fade,
    Slide,
    Pop,
    PunchZoom,
    Shake,
    Bounce,
    Pulse,
    Counter,
    Highlight,
}
