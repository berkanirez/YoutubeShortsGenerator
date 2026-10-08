using System.Text.Json;
using System.Text.Json.Nodes;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Application;

// The JSON Schema the LLM's scene-planning answer is constrained to while it's generated.
// Everything that is a closed set in this app becomes an enum here - scene types,
// animations, sfx and, importantly, the *actual* asset ids from the catalog - so the model
// cannot produce an unknown value at all, instead of producing one and being retried.
public static class VisualPlanSchema
{
    public static string Build(int sceneCount, IEnumerable<string> knownAssetIds)
    {
        var assetIds = new JsonArray(knownAssetIds.Select(id => (JsonNode?)JsonValue.Create(id)).ToArray());
        var sfx = new JsonArray(System.Enum.GetValues<SfxId>().Select(v => (JsonNode?)JsonNamingPolicy.CamelCase.ConvertName(v.ToString())).ToArray());
        sfx.Add(null);

        var scene = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["scene"] = new JsonObject { ["type"] = "integer" },
                ["type"] = EnumOf<SceneType>(name => name),
                ["onScreenText"] = new JsonObject { ["type"] = "string" },
                // An empty enum is an unsatisfiable grammar; with no catalog the only valid
                // answer is an empty array.
                ["assetIds"] = assetIds.Count == 0
                    ? new JsonObject { ["type"] = "array", ["maxItems"] = 0 }
                    : new JsonObject
                    {
                        ["type"] = "array",
                        ["maxItems"] = 1,
                        ["items"] = new JsonObject { ["type"] = "string", ["enum"] = assetIds },
                    },
                ["animation"] = EnumOf<AnimationId>(JsonNamingPolicy.CamelCase.ConvertName),
                ["sfx"] = new JsonObject { ["enum"] = sfx },
                ["emphasis"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
                ["secondaryText"] = new JsonObject { ["type"] = new JsonArray("string", "null") },
                ["items"] = new JsonObject
                {
                    ["type"] = "array",
                    ["maxItems"] = 3,
                    ["items"] = new JsonObject { ["type"] = "string" },
                },
            },
            ["required"] = new JsonArray(
                "scene", "type", "onScreenText", "assetIds", "animation", "sfx", "emphasis", "secondaryText", "items"),
        };

        var root = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["title"] = new JsonObject { ["type"] = "string" },
                ["hook"] = new JsonObject { ["type"] = "string" },
                ["takeaway"] = new JsonObject { ["type"] = "string" },
                ["musicMood"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray(MusicMood.All.Select(m => (JsonNode?)m).ToArray()),
                },
                ["scenes"] = new JsonObject
                {
                    ["type"] = "array",
                    ["minItems"] = sceneCount,
                    ["maxItems"] = sceneCount,
                    ["items"] = scene,
                },
            },
            ["required"] = new JsonArray("title", "hook", "takeaway", "musicMood", "scenes"),
        };

        return root.ToJsonString();
    }

    private static JsonObject EnumOf<T>(Func<string, string> spell) where T : struct, System.Enum => new()
    {
        ["type"] = "string",
        ["enum"] = new JsonArray(System.Enum.GetNames<T>().Select(name => (JsonNode?)spell(name)).ToArray()),
    };
}
