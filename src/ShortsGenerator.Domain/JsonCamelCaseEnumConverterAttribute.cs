using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShortsGenerator.Domain;

/// <summary>
/// Serializes an enum as camelCase JSON strings (e.g. <c>AnimationId.PunchZoom</c> -&gt;
/// <c>"punchZoom"</c>), matching the lowercase/camelCase animation and SFX IDs used in
/// CLAUDE.md, while the C# enum members themselves stay idiomatic PascalCase.
/// </summary>
public sealed class JsonCamelCaseEnumConverterAttribute : JsonConverterAttribute
{
    public override JsonConverter CreateConverter(Type typeToConvert) =>
        new JsonStringEnumConverter(JsonNamingPolicy.CamelCase);
}
