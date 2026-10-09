using System.Text;
using System.Text.Json;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Infrastructure.Assets;

public sealed record NewAsset(
    string Id,
    string SourceSvgPath,
    string Category,
    IReadOnlyList<string> Tags,
    IReadOnlyList<SceneType> CompatibleSceneTypes,
    AssetOrientation Orientation = AssetOrientation.Square,
    string? Anchor = "center");

// The add/replace workflow behind the AssetTool commands. Every change is checked before it
// touches anything (the SVG itself, then the whole catalog afterwards), so a bad file can
// never become part of the catalog the renderer inlines.
public sealed class AssetCatalogEditor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string _manifestPath;
    private readonly string _assetsRoot;

    public AssetCatalogEditor(string manifestPath)
    {
        _manifestPath = Path.GetFullPath(manifestPath);
        _assetsRoot = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(_manifestPath)!, ".."));
    }

    public AssetValidationReport Add(NewAsset asset)
    {
        var assets = Load();
        if (assets.Any(a => string.Equals(a.Id, asset.Id, StringComparison.OrdinalIgnoreCase)))
        {
            return Fail($"Asset '{asset.Id}' already exists; use 'replace' to swap its file.");
        }

        if (!AssetManifestValidator.FolderByCategory.TryGetValue(asset.Category, out var folder))
        {
            return Fail($"Unknown category '{asset.Category}' (allowed: {string.Join(", ", AssetManifestValidator.FolderByCategory.Keys)}).");
        }

        var svgProblems = CheckSource(asset.SourceSvgPath);
        if (svgProblems.Count > 0)
        {
            return new(svgProblems, [], assets.Count);
        }

        // Naming convention (docs/ASSETS.md): assets/<category folder>/<id>.<svg|png> - the file
        // name always comes from the validated id, never from the source file's name.
        var extension = Path.GetExtension(asset.SourceSvgPath).ToLowerInvariant();
        var target = Path.Combine(_assetsRoot, folder, $"{asset.Id}{extension}");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(asset.SourceSvgPath, target, overwrite: false);

        assets.Add(new AssetMetadata(
            asset.Id,
            $"../{folder}/{asset.Id}{extension}",
            asset.Category,
            asset.Tags.Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length > 0).ToList(),
            asset.CompatibleSceneTypes,
            asset.Orientation,
            asset.Anchor));
        return SaveAndValidate(assets, rollback: () => File.Delete(target));
    }

    public AssetValidationReport Replace(string id, string sourceSvgPath)
    {
        var assets = Load();
        var existing = assets.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            return Fail($"Asset '{id}' does not exist; use 'add'.");
        }

        var svgProblems = CheckSource(sourceSvgPath);
        if (svgProblems.Count > 0)
        {
            return new(svgProblems, [], assets.Count);
        }

        // Only the file changes: id, tags and scene types stay, so every existing plan that
        // references this id keeps working and simply renders the new drawing.
        var target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(_manifestPath)!, existing.FilePath));
        var extension = Path.GetExtension(sourceSvgPath).ToLowerInvariant();
        if (string.Equals(Path.GetExtension(target), extension, StringComparison.OrdinalIgnoreCase))
        {
            var backup = File.ReadAllBytes(target);
            File.Copy(sourceSvgPath, target, overwrite: true);
            return SaveAndValidate(assets, rollback: () => File.WriteAllBytes(target, backup));
        }

        // A different format (e.g. a hand-drawn SVG replaced by the user's PNG art): the file
        // moves to <id>.png and the manifest entry follows; the old file goes once all is valid.
        var newTarget = Path.ChangeExtension(target, extension);
        File.Copy(sourceSvgPath, newTarget, overwrite: true);
        var index = assets.IndexOf(existing);
        assets[index] = existing with { FilePath = Path.ChangeExtension(existing.FilePath, extension).Replace('\\', '/') };
        File.Move(target, target + ".replaced", overwrite: true);   // out of the way of the orphan check
        var report = SaveAndValidate(assets, rollback: () =>
        {
            File.Delete(newTarget);
            File.Move(target + ".replaced", target, overwrite: true);
        });
        if (report.IsValid)
        {
            File.Delete(target + ".replaced");
        }

        return report;
    }

    private List<string> CheckSource(string sourcePath) =>
        File.Exists(sourcePath)
            ? AssetManifestValidator.ValidateArt(sourcePath).Select(p => $"{Path.GetFileName(sourcePath)}: {p}").ToList()
            : [$"Source file '{sourcePath}' does not exist."];

    private List<AssetMetadata> Load() =>
        JsonSerializer.Deserialize<List<AssetMetadata>>(File.ReadAllText(_manifestPath), JsonOptions) ?? [];

    private AssetValidationReport SaveAndValidate(List<AssetMetadata> assets, Action rollback)
    {
        var previous = File.ReadAllText(_manifestPath);
        File.WriteAllText(_manifestPath, Serialize(assets), new UTF8Encoding(false));
        var report = AssetManifestValidator.Validate(_manifestPath);
        if (!report.IsValid)
        {
            // All or nothing: a change that leaves the catalog invalid is undone.
            File.WriteAllText(_manifestPath, previous, new UTF8Encoding(false));
            rollback();
        }

        return report;
    }

    private static AssetValidationReport Fail(string error) => new([error], [], 0);

    // Same layout as the hand-written manifest (one field per line, arrays inline), so adding
    // one asset shows up as one added block in a diff instead of a reformatted file.
    public static string Serialize(IReadOnlyList<AssetMetadata> assets)
    {
        static string Str(string? s) => s is null ? "null" : JsonSerializer.Serialize(s);
        static string Arr(IEnumerable<string> items) => "[" + string.Join(", ", items.Select(i => JsonSerializer.Serialize(i))) + "]";

        var sb = new StringBuilder("[\n");
        for (var i = 0; i < assets.Count; i++)
        {
            var a = assets[i];
            sb.Append("  {\n");
            sb.Append($"    \"id\": {Str(a.Id)},\n");
            sb.Append($"    \"filePath\": {Str(a.FilePath)},\n");
            sb.Append($"    \"category\": {Str(a.Category)},\n");
            sb.Append($"    \"tags\": {Arr(a.Tags)},\n");
            sb.Append($"    \"compatibleSceneTypes\": {Arr(a.CompatibleSceneTypes.Select(t => t.ToString()))},\n");
            sb.Append($"    \"orientation\": {Str(a.Orientation.ToString())},\n");
            sb.Append($"    \"anchor\": {Str(a.Anchor)}\n");
            sb.Append(i == assets.Count - 1 ? "  }\n" : "  },\n");
        }

        return sb.Append("]\n").ToString();
    }
}
