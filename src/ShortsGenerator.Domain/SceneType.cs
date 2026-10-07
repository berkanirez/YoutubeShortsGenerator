using System.Text.Json.Serialization;

namespace ShortsGenerator.Domain;

// The controlled vocabulary of scene types from CLAUDE.md section 7 — the LLM must pick
// one of these, it can never invent a new scene type.
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SceneType
{
    Hook,
    ExerciseDemo,
    Comparison,
    MythVsFact,
    AnatomyHighlight,
    Progress,
    List,
    Recovery,
    Outro,
}
