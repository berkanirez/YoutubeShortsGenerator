namespace ShortsGenerator.Domain;

// The controlled sound-effect IDs from CLAUDE.md section 13. No actual audio exists until
// Day 17 - this only constrains which IDs a scene is allowed to reference.
[JsonCamelCaseEnumConverter]
public enum SfxId
{
    Pop,
    Whoosh,
    Error,
    Success,
    Impact,
    Click,
}
