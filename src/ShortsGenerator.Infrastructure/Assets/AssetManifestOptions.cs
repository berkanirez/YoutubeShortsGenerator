namespace ShortsGenerator.Infrastructure.Assets;

public sealed class AssetManifestOptions
{
    public const string SectionName = "AssetManifest";

    // Relative to the Api project's content root (src/ShortsGenerator.Api), whatever folder
    // the process was started from - the repository's assets/ folder lives two levels up,
    // at the repo root, per CLAUDE.md's target structure.
    public string ManifestPath { get; set; } = "../../assets/manifests/assets.json";
}
