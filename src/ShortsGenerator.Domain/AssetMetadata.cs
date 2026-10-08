namespace ShortsGenerator.Domain;

public sealed record AssetMetadata(
    string Id,
    string FilePath,
    string Category,
    IReadOnlyList<string> Tags,
    IReadOnlyList<SceneType> CompatibleSceneTypes,
    AssetOrientation Orientation,
    string? Anchor);
