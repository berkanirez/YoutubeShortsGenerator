using System.Text.Json.Serialization;

namespace ShortsGenerator.Domain;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AssetOrientation
{
    Square,
    Portrait,
    Landscape,
    Any,
}
