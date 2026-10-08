using System.Text.Json;
using System.Text.RegularExpressions;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Infrastructure.Assets;

public sealed record AssetValidationReport(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings, int AssetCount)
{
    public bool IsValid => Errors.Count == 0;
}

// Everything that has to be true for an asset to be safe and usable, checked in one place
// (the `validate` command and every `add`/`replace` run it). Errors block; warnings are
// "probably a mistake" (an orphan file, half of a pose pair).
public static partial class AssetManifestValidator
{
    public static readonly IReadOnlyDictionary<string, string> FolderByCategory = new Dictionary<string, string>
    {
        ["mascot"] = "mascot",
        ["exercise"] = "exercises",
        ["equipment"] = "props",
        ["prop"] = "props",
        ["anatomy"] = "anatomy",
        ["icon"] = "icons",
    };

    // The renderer falls back to these when a scene has no asset (renderer/src/scenes/theme.ts).
    public static readonly IReadOnlyList<string> RequiredIds = ["mascot-idle", "mascot-thinking", "mascot-happy", "mascot-tired"];

    public const int MaxSvgBytes = 200 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static AssetValidationReport Validate(string manifestPath)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (!File.Exists(manifestPath))
        {
            return new([$"Manifest not found at '{manifestPath}'."], [], 0);
        }

        List<AssetMetadata>? assets;
        try
        {
            assets = JsonSerializer.Deserialize<List<AssetMetadata>>(File.ReadAllText(manifestPath), JsonOptions);
        }
        catch (JsonException ex)
        {
            return new([$"Manifest is not valid JSON: {ex.Message}"], [], 0);
        }

        assets ??= [];
        var manifestDirectory = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;
        var assetsRoot = Path.GetFullPath(Path.Combine(manifestDirectory, ".."));
        var referencedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var duplicate in assets.GroupBy(a => a.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
        {
            errors.Add($"Asset id '{duplicate.Key}' is used {duplicate.Count()} times.");
        }

        foreach (var asset in assets)
        {
            var label = $"Asset '{asset.Id}'";
            if (!AssetIdPattern().IsMatch(asset.Id ?? string.Empty))
            {
                errors.Add($"{label}: id must be lowercase letters, digits and '-' (e.g. 'pullup-top').");
            }

            if (!FolderByCategory.ContainsKey(asset.Category ?? string.Empty))
            {
                errors.Add($"{label}: unknown category '{asset.Category}' (allowed: {string.Join(", ", FolderByCategory.Keys)}).");
            }

            if (asset.Tags is null || asset.Tags.Count == 0)
            {
                errors.Add($"{label}: needs at least one tag (the LLM picks assets by their tags).");
            }
            else if (asset.Tags.Any(t => t != t.ToLowerInvariant() || t.Contains(' ')))
            {
                warnings.Add($"{label}: tags should be lowercase single words or kebab-case.");
            }

            if (asset.CompatibleSceneTypes is null || asset.CompatibleSceneTypes.Count == 0)
            {
                warnings.Add($"{label}: no compatible scene types listed.");
            }

            var fullPath = Path.GetFullPath(Path.Combine(manifestDirectory, asset.FilePath ?? string.Empty));
            if (!fullPath.StartsWith(assetsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"{label}: file path '{asset.FilePath}' points outside the assets folder.");
                continue;
            }

            referencedFiles.Add(fullPath);
            if (!File.Exists(fullPath))
            {
                errors.Add($"{label}: file '{asset.FilePath}' does not exist.");
                continue;
            }

            errors.AddRange(ValidateSvg(fullPath).Select(problem => $"{label}: {problem}"));
        }

        foreach (var required in RequiredIds.Where(id => !assets.Any(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase))))
        {
            errors.Add($"Required asset '{required}' is missing (the renderer uses it as a fallback).");
        }

        var ids = assets.Select(a => a.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids.Where(id => id.EndsWith("-top", StringComparison.Ordinal) || id.EndsWith("-bottom", StringComparison.Ordinal)))
        {
            var partner = id.EndsWith("-top", StringComparison.Ordinal) ? id[..^4] + "-bottom" : id[..^7] + "-top";
            if (!ids.Contains(partner))
            {
                warnings.Add($"Asset '{id}' has no '{partner}' pose, so ExerciseDemo can't animate a rep with it.");
            }
        }

        foreach (var orphan in Directory.EnumerateFiles(assetsRoot, "*.svg", SearchOption.AllDirectories)
                     .Where(file => !referencedFiles.Contains(Path.GetFullPath(file))))
        {
            warnings.Add($"File '{Path.GetRelativePath(assetsRoot, orphan)}' is not in the manifest.");
        }

        return new(errors, warnings, assets.Count);
    }

    // The renderer inlines SVG markup into the page (dangerouslySetInnerHTML), so an asset
    // file is effectively code. Anything that can execute or load from outside is refused.
    public static IReadOnlyList<string> ValidateSvg(string path)
    {
        var problems = new List<string>();
        if (!path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            problems.Add("only .svg files are supported.");
            return problems;
        }

        var bytes = new FileInfo(path).Length;
        if (bytes > MaxSvgBytes)
        {
            problems.Add($"file is {bytes / 1024} kB; keep SVGs under {MaxSvgBytes / 1024} kB (simplify the drawing).");
        }

        var svg = File.ReadAllText(path);
        if (!SvgRoot().IsMatch(svg))
        {
            problems.Add("not an SVG (no <svg> root element).");
            return problems;
        }

        if (!ViewBox().IsMatch(svg))
        {
            problems.Add("the <svg> element needs a viewBox so it can scale to any size.");
        }

        if (ScriptTag().IsMatch(svg) || EventAttribute().IsMatch(svg) || JavascriptUrl().IsMatch(svg))
        {
            problems.Add("contains script or event handlers (not allowed - SVGs are inlined into the renderer page).");
        }

        if (ForeignContent().IsMatch(svg))
        {
            problems.Add("contains <foreignObject>/<iframe>/<embed> (not allowed).");
        }

        if (ExternalReference().IsMatch(svg))
        {
            problems.Add("references an external URL (renders must work offline and be self-contained).");
        }

        return problems;
    }

    [GeneratedRegex(@"^[a-z0-9][a-z0-9-]*$")]
    private static partial Regex AssetIdPattern();

    [GeneratedRegex(@"<svg[\s>]", RegexOptions.IgnoreCase)]
    private static partial Regex SvgRoot();

    [GeneratedRegex(@"<svg[^>]*\sviewBox\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex ViewBox();

    [GeneratedRegex(@"<\s*script", RegexOptions.IgnoreCase)]
    private static partial Regex ScriptTag();

    [GeneratedRegex(@"\son[a-z]+\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex EventAttribute();

    [GeneratedRegex(@"javascript:", RegexOptions.IgnoreCase)]
    private static partial Regex JavascriptUrl();

    [GeneratedRegex(@"<\s*(foreignObject|iframe|embed|object)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ForeignContent();

    [GeneratedRegex(@"(href|src)\s*=\s*[""']\s*(https?:|//|data:text)", RegexOptions.IgnoreCase)]
    private static partial Regex ExternalReference();
}
