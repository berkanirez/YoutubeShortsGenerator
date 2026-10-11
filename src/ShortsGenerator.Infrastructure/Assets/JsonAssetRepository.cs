using System.Text.Json;
using Microsoft.Extensions.Options;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Infrastructure.Assets;

public sealed class JsonAssetRepository : IAssetRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly string _path;
    private readonly object _gate = new();
    private Snapshot _snapshot;

    private sealed record Snapshot(DateTime Stamp, IReadOnlyList<AssetMetadata> Assets, IReadOnlyDictionary<string, AssetMetadata> ById);

    // Loads and validates the manifest once, at construction (this is registered as a
    // singleton) - a broken/missing manifest fails the app at startup with a clear error,
    // the same "fail fast" treatment Program.cs already gives EF Core migrations, instead
    // of surfacing a confusing error on the first real scene-planning request.
    public JsonAssetRepository(IOptions<AssetManifestOptions> options)
    {
        _path = options.Value.ManifestPath;
        _snapshot = Load(_path);
    }

    private static Snapshot Load(string path)
    {
        var stamp = File.GetLastWriteTimeUtc(path);
        if (!File.Exists(path))
        {
            throw new InvalidOperationException(
                $"Asset manifest not found at '{Path.GetFullPath(path)}'. " +
                "Check AssetManifest:ManifestPath in appsettings.json.");
        }

        var json = File.ReadAllText(path);
        List<AssetMetadata>? rawAssets;
        try
        {
            rawAssets = JsonSerializer.Deserialize<List<AssetMetadata>>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Asset manifest at '{path}' is not valid JSON.", ex);
        }

        if (rawAssets is null || rawAssets.Count == 0)
        {
            throw new InvalidOperationException($"Asset manifest at '{path}' is empty.");
        }

        // The manifest's own filePath entries are relative to the manifest file itself
        // (e.g. "../mascot/mascot-idle.svg"), not to whatever the current process's working
        // directory happens to be - resolving to an absolute path here means every future
        // consumer (this API today, the Remotion renderer from Day 7) sees an unambiguous
        // path regardless of where it runs from.
        var manifestDirectory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var assets = rawAssets
            .Select(a => a with { FilePath = Path.GetFullPath(Path.Combine(manifestDirectory, a.FilePath)) })
            .ToList();

        foreach (var asset in assets)
        {
            if (!File.Exists(asset.FilePath))
            {
                throw new InvalidOperationException(
                    $"Asset '{asset.Id}' in the manifest points to a missing file: '{asset.FilePath}'.");
            }
        }

        return new Snapshot(stamp, assets, assets.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase));
    }

    // Art can now be added while the API runs (ArtImporter, the AssetTool), so the manifest is
    // re-read when its file changes. A broken edit keeps the last good catalog; the editors
    // validate and roll back, so that would be a hand edit gone wrong.
    private Snapshot Current()
    {
        lock (_gate)
        {
            if (File.GetLastWriteTimeUtc(_path) != _snapshot.Stamp)
            {
                try
                {
                    _snapshot = Load(_path);
                }
                catch (InvalidOperationException)
                {
                }
            }

            return _snapshot;
        }
    }

    public Task<IReadOnlyList<AssetMetadata>> GetAllAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Current().Assets);

    public Task<AssetMetadata?> GetByIdAsync(string id, CancellationToken cancellationToken) =>
        Task.FromResult(Current().ById.GetValueOrDefault(id));
}
