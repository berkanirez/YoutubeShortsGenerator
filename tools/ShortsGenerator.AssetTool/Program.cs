using ShortsGenerator.Domain;
using ShortsGenerator.Infrastructure.Assets;

// Asset library maintenance - adding art never requires touching application code:
//   dotnet run --project tools/ShortsGenerator.AssetTool -- validate
//   dotnet run --project tools/ShortsGenerator.AssetTool -- add --id quads-highlight --file my.svg
//       --category anatomy --tags quads,legs,squat --scene-types AnatomyHighlight
//   dotnet run --project tools/ShortsGenerator.AssetTool -- replace --id dumbbell --file better.svg
//   dotnet run --project tools/ShortsGenerator.AssetTool -- index [--out output/asset-index.html]
// Options available on every command: --manifest <path> (default: assets/manifests/assets.json
// under the repository root, found by walking up from the current folder).

return Run(args);

static int Run(string[] args)
{
    if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
    {
        Console.WriteLine("Usage: validate | add --id --file --category --tags a,b [--scene-types A,B] [--orientation Square] | replace --id --file | index [--out path]   (all: [--manifest path])");
        return args.Length == 0 ? 1 : 0;
    }

    var options = ParseOptions(args.Skip(1).ToArray());
    var manifest = options.GetValueOrDefault("manifest") ?? FindDefaultManifest();
    if (manifest is null)
    {
        Console.Error.WriteLine("Could not find assets/manifests/assets.json; pass --manifest <path>.");
        return 1;
    }

    try
    {
        switch (args[0])
        {
            case "validate":
                return Print(AssetManifestValidator.Validate(manifest));

            case "add":
                return Print(new AssetCatalogEditor(manifest).Add(new NewAsset(
                    Required(options, "id"),
                    Required(options, "file"),
                    Required(options, "category"),
                    Required(options, "tags").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                    (options.GetValueOrDefault("scene-types") ?? string.Empty)
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(t => Enum.Parse<SceneType>(t, ignoreCase: true))
                        .ToList(),
                    Enum.Parse<AssetOrientation>(options.GetValueOrDefault("orientation") ?? "Square", ignoreCase: true))));

            case "replace":
                return Print(new AssetCatalogEditor(manifest).Replace(Required(options, "id"), Required(options, "file")));

            case "index":
                var output = options.GetValueOrDefault("out")
                    ?? Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(manifest))!)!)!, "output", "asset-index.html");
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                File.WriteAllText(output, AssetIndexPage.Build(manifest));
                Console.WriteLine($"Wrote {output}");
                return 0;

            default:
                Console.Error.WriteLine($"Unknown command '{args[0]}'. Run with --help.");
                return 1;
        }
    }
    catch (Exception ex) when (ex is ArgumentException or IOException)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
}

static int Print(AssetValidationReport report)
{
    foreach (var error in report.Errors)
    {
        Console.WriteLine($"ERROR   {error}");
    }

    foreach (var warning in report.Warnings)
    {
        Console.WriteLine($"WARNING {warning}");
    }

    Console.WriteLine(report.IsValid
        ? $"OK - {report.AssetCount} assets, {report.Warnings.Count} warning(s)."
        : $"FAILED - {report.Errors.Count} error(s). Nothing was changed.");
    return report.IsValid ? 0 : 1;
}

static Dictionary<string, string> ParseOptions(string[] args)
{
    var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i].StartsWith("--", StringComparison.Ordinal) && i + 1 < args.Length)
        {
            options[args[i][2..]] = args[++i];
        }
    }

    return options;
}

static string Required(Dictionary<string, string> options, string name) =>
    options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
        ? value
        : throw new ArgumentException($"--{name} is required.");

static string? FindDefaultManifest()
{
    for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
    {
        var candidate = Path.Combine(directory.FullName, "assets", "manifests", "assets.json");
        if (File.Exists(candidate))
        {
            return candidate;
        }
    }

    return null;
}
