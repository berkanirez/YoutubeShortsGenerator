using ShortsGenerator.Domain;
using ShortsGenerator.Infrastructure.Assets;

namespace ShortsGenerator.Infrastructure.Tests;

// The asset add/replace/validate workflow against a throwaway copy of a catalog.
public sealed class AssetToolingTests : IDisposable
{
    private const string GoodSvg = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><circle cx="5" cy="5" r="4"/></svg>""";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "asset-tool-tests-" + Guid.NewGuid());
    private readonly string _manifest;

    public AssetToolingTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "assets", "manifests"));
        Directory.CreateDirectory(Path.Combine(_root, "assets", "mascot"));
        var required = AssetManifestValidator.RequiredIds
            .Select(id => new AssetMetadata(id, $"../mascot/{id}.svg", "mascot", ["mascot"], [SceneType.Hook], AssetOrientation.Square, "center"))
            .ToList();
        foreach (var id in AssetManifestValidator.RequiredIds)
        {
            File.WriteAllText(Path.Combine(_root, "assets", "mascot", $"{id}.svg"), GoodSvg);
        }

        _manifest = Path.Combine(_root, "assets", "manifests", "assets.json");
        File.WriteAllText(_manifest, AssetCatalogEditor.Serialize(required));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Validate_CleanCatalog_IsValid()
    {
        var report = AssetManifestValidator.Validate(_manifest);

        Assert.True(report.IsValid, string.Join("; ", report.Errors));
        Assert.Equal(4, report.AssetCount);
    }

    [Fact]
    public void Add_CopiesTheFileByNamingConvention_AndUpdatesTheManifest()
    {
        var report = new AssetCatalogEditor(_manifest).Add(new NewAsset(
            "quads-highlight", Source("my drawing.svg", GoodSvg), "anatomy", ["Quads", " legs "], [SceneType.AnatomyHighlight]));

        Assert.True(report.IsValid, string.Join("; ", report.Errors));
        Assert.True(File.Exists(Path.Combine(_root, "assets", "anatomy", "quads-highlight.svg")));
        var manifest = File.ReadAllText(_manifest);
        Assert.Contains("\"filePath\": \"../anatomy/quads-highlight.svg\"", manifest);
        Assert.Contains("\"tags\": [\"quads\", \"legs\"]", manifest);
    }

    [Theory]
    [InlineData("""<svg viewBox="0 0 1 1" onload="alert(1)"></svg>""", "script or event handlers")]
    [InlineData("""<svg viewBox="0 0 1 1"><script>x()</script></svg>""", "script or event handlers")]
    [InlineData("""<svg viewBox="0 0 1 1"><a href="javascript:x()"/></svg>""", "script or event handlers")]
    [InlineData("""<svg viewBox="0 0 1 1"><image href="https://example.com/t.png"/></svg>""", "external URL")]
    [InlineData("""<svg viewBox="0 0 1 1"><foreignObject/></svg>""", "foreignObject")]
    [InlineData("""<svg width="10" height="10"></svg>""", "viewBox")]
    [InlineData("""<html>not svg</html>""", "not an SVG")]
    public void Add_UnsafeOrBrokenSvg_IsRejected_AndNothingChanges(string svg, string expected)
    {
        var before = File.ReadAllText(_manifest);

        var report = new AssetCatalogEditor(_manifest).Add(new NewAsset(
            "bad-icon", Source("bad.svg", svg), "icon", ["test"], [SceneType.List]));

        Assert.False(report.IsValid);
        Assert.Contains(report.Errors, e => e.Contains(expected));
        Assert.Equal(before, File.ReadAllText(_manifest));
        Assert.False(Directory.Exists(Path.Combine(_root, "assets", "icons")));
    }

    [Fact]
    public void Add_ExistingId_IsRejected_SoReplaceIsUsedInstead()
    {
        var report = new AssetCatalogEditor(_manifest).Add(new NewAsset(
            "mascot-idle", Source("x.svg", GoodSvg), "mascot", ["mascot"], [SceneType.Hook]));

        Assert.Contains(report.Errors, e => e.Contains("use 'replace'"));
    }

    [Theory]
    [InlineData("Quads_Highlight")]
    [InlineData("../escape")]
    public void Add_InvalidId_IsRolledBack(string id)
    {
        var before = File.ReadAllText(_manifest);

        var report = new AssetCatalogEditor(_manifest).Add(new NewAsset(id, Source("x.svg", GoodSvg), "icon", ["x"], [SceneType.List]));

        Assert.False(report.IsValid);
        Assert.Equal(before, File.ReadAllText(_manifest));
    }

    [Fact]
    public void Replace_SwapsOnlyTheDrawing_KeepingIdAndMetadata()
    {
        const string newDrawing = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><rect width="9" height="9"/></svg>""";
        var before = File.ReadAllText(_manifest);

        var report = new AssetCatalogEditor(_manifest).Replace("mascot-happy", Source("new.svg", newDrawing));

        Assert.True(report.IsValid);
        Assert.Equal(newDrawing, File.ReadAllText(Path.Combine(_root, "assets", "mascot", "mascot-happy.svg")));
        Assert.Equal(before, File.ReadAllText(_manifest));
    }

    [Fact]
    public void Validate_FlagsMissingFallbacks_OrphanFiles_AndHalfPosePairs()
    {
        var editor = new AssetCatalogEditor(_manifest);
        editor.Add(new NewAsset("squat-top", Source("s.svg", GoodSvg), "exercise", ["squat"], [SceneType.ExerciseDemo]));
        File.WriteAllText(Path.Combine(_root, "assets", "mascot", "stray.svg"), GoodSvg);
        File.Delete(Path.Combine(_root, "assets", "mascot", "mascot-tired.svg"));

        var report = AssetManifestValidator.Validate(_manifest);

        Assert.Contains(report.Errors, e => e.Contains("'mascot-tired'") && e.Contains("does not exist"));
        Assert.Contains(report.Warnings, w => w.Contains("no 'squat-bottom' pose"));
        Assert.Contains(report.Warnings, w => w.Contains("stray.svg") && w.Contains("not in the manifest"));
    }

    [Fact]
    public void Serialize_RoundTripsThroughTheRealReader()
    {
        var assets = new List<AssetMetadata>
        {
            new("a-b", "../icons/a-b.svg", "icon", ["x", "y"], [SceneType.List, SceneType.Recovery], AssetOrientation.Square, null),
        };

        var parsed = System.Text.Json.JsonSerializer.Deserialize<List<AssetMetadata>>(
            AssetCatalogEditor.Serialize(assets), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        Assert.Equal(assets[0] with { Tags = [], CompatibleSceneTypes = [] }, parsed![0] with { Tags = [], CompatibleSceneTypes = [] });
        Assert.Equal(["x", "y"], parsed[0].Tags);
        Assert.Equal([SceneType.List, SceneType.Recovery], parsed[0].CompatibleSceneTypes);
    }

    [Fact]
    public void IndexPage_ShowsEveryAssetWithItsArtAndEscapedMetadata()
    {
        var html = AssetIndexPage.Build(_manifest);

        Assert.Contains("Asset index (4)", html);
        Assert.Contains("mascot-idle", html);
        Assert.Contains("<circle", html);
    }

    private string Source(string name, string content)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }
}
