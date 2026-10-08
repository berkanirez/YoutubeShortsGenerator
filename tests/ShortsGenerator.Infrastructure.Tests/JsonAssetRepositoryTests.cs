using Microsoft.Extensions.Options;
using ShortsGenerator.Infrastructure.Assets;

namespace ShortsGenerator.Infrastructure.Tests;

public class JsonAssetRepositoryTests : IDisposable
{
    private readonly string _tempDirectory;

    public JsonAssetRepositoryTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"shortsgenerator-assets-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public async Task GetAllAsync_ValidManifest_ReturnsAssetsWithResolvedFilePaths()
    {
        var svgPath = Path.Combine(_tempDirectory, "mascot-idle.svg");
        File.WriteAllText(svgPath, "<svg></svg>");

        var manifestPath = Path.Combine(_tempDirectory, "assets.json");
        File.WriteAllText(manifestPath, """
            [
              {
                "id": "mascot-idle",
                "filePath": "mascot-idle.svg",
                "category": "mascot",
                "tags": ["mascot"],
                "compatibleSceneTypes": ["Hook"],
                "orientation": "Square",
                "anchor": "center"
              }
            ]
            """);

        var repository = new JsonAssetRepository(Options.Create(new AssetManifestOptions { ManifestPath = manifestPath }));

        var assets = await repository.GetAllAsync(CancellationToken.None);

        Assert.Single(assets);
        Assert.Equal("mascot-idle", assets[0].Id);
        Assert.Equal(Path.GetFullPath(svgPath), assets[0].FilePath);
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        var manifestPath = WriteMinimalManifest();
        var repository = new JsonAssetRepository(Options.Create(new AssetManifestOptions { ManifestPath = manifestPath }));

        var result = await repository.GetByIdAsync("does-not-exist", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public void Constructor_MissingManifestFile_ThrowsClearError()
    {
        var options = Options.Create(new AssetManifestOptions { ManifestPath = Path.Combine(_tempDirectory, "missing.json") });

        var ex = Assert.Throws<InvalidOperationException>(() => new JsonAssetRepository(options));
        Assert.Contains("not found", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Constructor_ManifestReferencesMissingFile_ThrowsClearError()
    {
        var manifestPath = Path.Combine(_tempDirectory, "assets.json");
        File.WriteAllText(manifestPath, """
            [
              {
                "id": "ghost-asset",
                "filePath": "does-not-exist.svg",
                "category": "mascot",
                "tags": [],
                "compatibleSceneTypes": [],
                "orientation": "Square",
                "anchor": null
              }
            ]
            """);
        var options = Options.Create(new AssetManifestOptions { ManifestPath = manifestPath });

        var ex = Assert.Throws<InvalidOperationException>(() => new JsonAssetRepository(options));
        Assert.Contains("ghost-asset", ex.Message);
        Assert.Contains("missing file", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private string WriteMinimalManifest()
    {
        var svgPath = Path.Combine(_tempDirectory, "mascot-idle.svg");
        File.WriteAllText(svgPath, "<svg></svg>");

        var manifestPath = Path.Combine(_tempDirectory, "assets.json");
        File.WriteAllText(manifestPath, """
            [
              {
                "id": "mascot-idle",
                "filePath": "mascot-idle.svg",
                "category": "mascot",
                "tags": [],
                "compatibleSceneTypes": [],
                "orientation": "Square",
                "anchor": null
              }
            ]
            """);
        return manifestPath;
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }
}
