using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ShortsGenerator.Domain;
using ShortsGenerator.Infrastructure.Art;
using ShortsGenerator.Infrastructure.Assets;
using ShortsGenerator.Infrastructure.Processes;

// Asset library maintenance - adding art never requires touching application code:
//   dotnet run --project tools/ShortsGenerator.AssetTool -- validate
//   dotnet run --project tools/ShortsGenerator.AssetTool -- add --id quads-highlight --file my.svg
//       --category anatomy --tags quads,legs,squat --scene-types AnatomyHighlight
//   dotnet run --project tools/ShortsGenerator.AssetTool -- replace --id dumbbell --file better.svg
//   dotnet run --project tools/ShortsGenerator.AssetTool -- index [--out output/asset-index.html]
//   dotnet run --project tools/ShortsGenerator.AssetTool -- prompts [--out docs/ART_PROMPTS_MISSING.md]
//       every picture subjects.json knows but the catalog lacks, with its ready prompt and file name
//   dotnet run --project tools/ShortsGenerator.AssetTool -- import
//       imports every such picture found in assets/incoming/ (HD upscale, cut-out, catalog)
// Options available on every command: --manifest <path> (default: assets/manifests/assets.json
// under the repository root, found by walking up from the current folder).

return Run(args);

static int Run(string[] args)
{
    if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
    {
        Console.WriteLine("Usage: validate | add --id --file --category --tags a,b [--scene-types A,B] [--orientation Square] | replace --id --file | index [--out path] | prompts [--out path] | import   (all: [--manifest path])");
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

            case "prompts":
            {
                var missing = MissingSubjects(manifest);
                var (sheets, singles) = ArtSheetPlanner.Plan(missing);
                var file = options.GetValueOrDefault("out") ?? Path.Combine(RepoRoot(manifest), "docs", "ART_PROMPTS_MISSING.md");
                File.WriteAllText(file, PromptSheet(missing.Count, sheets, singles));
                SaveSheetMap(manifest, sheets);
                Console.WriteLine($"{missing.Count} picture(s) missing -> {sheets.Count + singles.Count} prompt(s) " +
                                  $"({sheets.Count} sheets, {singles.Count} single); written to {file}");
                return 0;
            }

            case "import":
            {
                var root = RepoRoot(manifest);
                var importer = new ArtImporter(
                    new ProcessRunner(),
                    Options.Create(new ArtImportOptions
                    {
                        IncomingDirectory = Path.Combine(root, "assets", "incoming"),
                        WorkDirectory = Path.Combine(root, "tools", "images", ".cache", "import"),
                        PythonPath = Path.Combine(root, "tools", "images", ".venv", "Scripts", "python.exe"),
                        PrepareScriptPath = Path.Combine(root, "tools", "images", "prepare_art.py"),
                        UpscalerPath = Path.Combine(root, "tools", "images", "realesrgan", "realesrgan-ncnn-vulkan.exe"),
                    }),
                    Options.Create(new AssetManifestOptions { ManifestPath = manifest }),
                    NullLogger<ArtImporter>.Instance);
                var failed = SplitSheets(manifest, importer);
                foreach (var subject in MissingSubjects(manifest).Where(importer.HasSource))
                {
                    try
                    {
                        var added = importer.ImportAsync(subject, CancellationToken.None).GetAwaiter().GetResult();
                        Console.WriteLine($"OK      {subject.IncomingFileName} -> {string.Join(", ", added)}");
                    }
                    catch (ArtImportException ex)
                    {
                        failed++;
                        Console.WriteLine($"FAILED  {subject.IncomingFileName}: {ex.Message}");
                    }
                }

                Console.WriteLine($"{MissingSubjects(manifest).Count} picture(s) still missing.");
                return failed == 0 ? Print(AssetManifestValidator.Validate(manifest)) : 1;
            }

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

static string RepoRoot(string manifest) =>
    Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(manifest))!, "..", ".."));

static IReadOnlyList<ArtSubject> MissingSubjects(string manifest)
{
    var subjects = JsonArtSubjectCatalog.Load(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(manifest))!, "subjects.json"));
    var existing = new AssetCatalogEditor(manifest).ExistingIds();
    return subjects.Where(s => !s.AssetIds.All(existing.Contains)).ToList();
}

// The prompts to paste, in the order worth doing them: sheets first (several pictures per
// prompt), then the single ones. Each prompt is built by ArtSheetPlanner / ArtPromptBuilder -
// the same wording a video asks for.
static string PromptSheet(int missingCount, IReadOnlyList<ArtSheet> sheets, IReadOnlyList<ArtSubject> singles)
{
    var sb = new System.Text.StringBuilder();
    sb.AppendLine("# Missing pictures - ready prompts");
    sb.AppendLine();
    sb.AppendLine($"Generated by `AssetTool prompts` from assets/manifests/subjects.json: {missingCount} picture(s) the catalog");
    sb.AppendLine($"doesn't have yet, in {sheets.Count + singles.Count} prompt(s). Paste each prompt into the image tool (attach");
    sb.AppendLine("`assets/mascot/mascot-idle.png` as the reference where it says so) and save the result under the file name");
    sb.AppendLine("shown into `assets/incoming/`. Then run `dotnet run --project tools/ShortsGenerator.AssetTool -- import`");
    sb.AppendLine("(or ask Claude Code to import them): sheets are cut into their pictures automatically.");
    var number = 0;
    if (sheets.Count > 0)
    {
        sb.AppendLine();
        sb.AppendLine($"## Sheets - several pictures per prompt ({sheets.Count})");
        foreach (var sheet in sheets)
        {
            number++;
            sb.AppendLine();
            sb.AppendLine($"### {number}. `{sheet.FileName}` - {sheet.Subjects.Count} pictures" +
                          (sheet.Subjects.Any(ArtPromptBuilder.NeedsReferenceImage) ? " - attach the reference mascot" : string.Empty));
            sb.AppendLine();
            sb.AppendLine($"Contains: {string.Join(", ", sheet.Subjects.Select(s => s.Id))}");
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine(sheet.Prompt);
            sb.AppendLine("```");
        }
    }

    if (singles.Count > 0)
    {
        sb.AppendLine();
        sb.AppendLine($"## One picture per prompt ({singles.Count}) - exercise pairs: both poses in ONE wide image, split automatically");
        foreach (var subject in singles)
        {
            number++;
            sb.AppendLine();
            sb.AppendLine($"### {number}. `{subject.IncomingFileName}`" +
                          (ArtPromptBuilder.NeedsReferenceImage(subject) ? " - attach the reference mascot" : string.Empty));
            sb.AppendLine();
            sb.AppendLine("```");
            sb.AppendLine(ArtPromptBuilder.Build(subject));
            sb.AppendLine("```");
        }
    }

    return sb.ToString();
}

// Which picture is where on each sheet, kept in assets/incoming/sheets.json for the import.
// Merged, never rewritten: a sheet generated from an earlier prompt list must still be cut the
// way that list described it.
static string SheetMapPath(string manifest) => Path.Combine(RepoRoot(manifest), "assets", "incoming", "sheets.json");

static void SaveSheetMap(string manifest, IReadOnlyList<ArtSheet> sheets)
{
    var map = LoadSheetMap(manifest);
    foreach (var sheet in sheets)
    {
        map.TryAdd(sheet.FileName, new SheetEntry(sheet.Rows, sheet.Cols, sheet.Subjects.Select(s => s.Id).ToList()));
    }

    Directory.CreateDirectory(Path.GetDirectoryName(SheetMapPath(manifest))!);
    File.WriteAllText(SheetMapPath(manifest), System.Text.Json.JsonSerializer.Serialize(map, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
}

static Dictionary<string, SheetEntry> LoadSheetMap(string manifest) =>
    File.Exists(SheetMapPath(manifest))
        ? System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, SheetEntry>>(File.ReadAllText(SheetMapPath(manifest))) ?? []
        : [];

// Cuts every sheet present in assets/incoming into the incoming files of its pictures (only the
// ones still missing a file), so the normal import picks them up. Returns the number of failures.
static int SplitSheets(string manifest, ArtImporter importer)
{
    var root = RepoRoot(manifest);
    var incoming = Path.Combine(root, "assets", "incoming");
    var subjects = JsonArtSubjectCatalog.Load(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(manifest))!, "subjects.json"))
        .ToDictionary(s => s.Id);
    var failed = 0;
    foreach (var (fileName, entry) in LoadSheetMap(manifest))
    {
        var sheetPath = Path.Combine(incoming, fileName);
        var onSheet = entry.Subjects.Select(id => subjects.GetValueOrDefault(id)).ToList();
        if (!File.Exists(sheetPath) || onSheet.All(s => s is null || importer.HasSource(s)))
        {
            continue;
        }

        // Every panel gets an output in order; panels whose picture is already there are cut to a scratch file.
        var scratch = Path.Combine(root, "tools", "images", ".cache", "sheets", Path.GetFileNameWithoutExtension(fileName));
        Directory.CreateDirectory(scratch);
        var outputs = onSheet.Select((s, i) => s is not null && !importer.HasSource(s)
            ? Path.Combine(incoming, s.IncomingFileName)
            : Path.Combine(scratch, $"panel-{i + 1}.png")).ToList();
        var result = new ProcessRunner().RunAsync(
            new ProcessSpec(
                Path.Combine(root, "tools", "images", ".venv", "Scripts", "python.exe"),
                onSheet.All(s => s?.Kind == ArtKind.ExercisePair)
                    ? [Path.Combine(root, "tools", "images", "split_sheet.py"), "--pairs", sheetPath, entry.Rows.ToString(), .. outputs]
                    : [Path.Combine(root, "tools", "images", "split_sheet.py"), sheetPath, entry.Rows.ToString(), entry.Cols.ToString(), .. outputs],
                null,
                TimeSpan.FromMinutes(2)),
            CancellationToken.None).GetAwaiter().GetResult();
        if (result.ExitCode == 0)
        {
            Console.WriteLine($"CUT     {fileName} -> {string.Join(", ", entry.Subjects)}");
        }
        else
        {
            failed++;
            Console.WriteLine($"FAILED  {fileName}: {result.StandardError.Trim()}");
        }
    }

    return failed;
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

sealed record SheetEntry(int Rows, int Cols, List<string> Subjects);
