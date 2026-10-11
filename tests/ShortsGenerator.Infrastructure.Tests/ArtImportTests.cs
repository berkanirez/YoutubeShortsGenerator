using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ShortsGenerator.Domain;
using ShortsGenerator.Infrastructure.Art;
using ShortsGenerator.Infrastructure.Assets;
using ShortsGenerator.Infrastructure.Processes;

namespace ShortsGenerator.Infrastructure.Tests;

public sealed class ArtImportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"art-import-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ShortsGenerator.slnx")))
        {
            directory = directory.Parent;
        }

        return directory!.FullName;
    }

    [Fact]
    public void TheRealSubjectsFile_LoadsWithoutProblems()
    {
        var subjects = JsonArtSubjectCatalog.Load(Path.Combine(RepoRoot(), "assets", "manifests", "subjects.json"));

        Assert.True(subjects.Count >= 40);
        Assert.Contains(subjects, s => s.Id == "forearms" && s.Kind == ArtKind.Muscle);
    }

    [Fact]
    public void EveryExerciseMuscleAndIconInTheCatalog_HasASubject()
    {
        // Otherwise a scene about it could never be matched to it - and would ask for art that exists.
        var root = RepoRoot();
        var subjects = JsonArtSubjectCatalog.Load(Path.Combine(root, "assets", "manifests", "subjects.json"));
        var known = subjects.SelectMany(s => s.AssetIds).ToHashSet();
        var catalog = JsonSerializer.Deserialize<List<AssetMetadata>>(
            File.ReadAllText(Path.Combine(root, "assets", "manifests", "assets.json")),
            new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } })!;

        var uncovered = catalog.Where(a => a.Category != "mascot" && !known.Contains(a.Id)).Select(a => a.Id).ToList();

        Assert.Empty(uncovered);
    }

    [Fact]
    public void ABrokenSubjectsFile_FailsWithTheReason()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "subjects.json");
        File.WriteAllText(path, """[{ "id": "../evil", "kind": "Icon", "terms": ["x"], "describe": "x" }, { "id": "squat", "kind": "ExercisePair", "terms": ["squat"], "describe": "squat" }]""");

        var ex = Assert.Throws<InvalidOperationException>(() => JsonArtSubjectCatalog.Load(path));

        Assert.Contains("'../evil': ids must be lowercase kebab-case", ex.Message);
        Assert.Contains("'squat': an ExercisePair needs top and bottom poses", ex.Message);
    }

    [Fact]
    public async Task Import_APair_UpscalesSplitsAndRegistersBothPoses()
    {
        var (importer, runner, manifest) = CreateImporter(upscaler: true);
        var subject = new ArtSubject("kettlebell-swing", ArtKind.ExercisePair, ["kettlebell swing"], "kettlebell swing", "top", "bottom");
        await importer.SaveSourceAsync(subject, new MemoryStream(TinyPng()), CancellationToken.None);

        var added = await importer.ImportAsync(subject, CancellationToken.None);

        Assert.Equal(["kettlebell-swing-top", "kettlebell-swing-bottom"], added);
        Assert.Equal(3, runner.Calls.Count);   // upscale, prepare, clean holes
        Assert.Contains("--apply", runner.Calls[2].Arguments);
        Assert.Equal(["-i", Path.Combine(_root, "incoming", "kettlebell-swing-pair.png")], runner.Calls[0].Arguments.Take(2));
        Assert.Contains("--pair", runner.Calls[1].Arguments);
        var ids = new AssetCatalogEditor(manifest).ExistingIds();
        Assert.Contains("kettlebell-swing-top", ids);
        Assert.Contains("kettlebell-swing-bottom", ids);
    }

    [Fact]
    public async Task Import_AnIconWithoutTheUpscaler_KeepsItsWhitePartsAndStillWorks()
    {
        var (importer, runner, _) = CreateImporter(upscaler: false);
        var subject = new ArtSubject("water", ArtKind.Icon, ["water"], "a water bottle");
        await importer.SaveSourceAsync(subject, new MemoryStream(TinyPng()), CancellationToken.None);

        var added = await importer.ImportAsync(subject, CancellationToken.None);

        Assert.Equal(["water"], added);
        var call = Assert.Single(runner.Calls);
        Assert.Contains("--keep-holes", call.Arguments);
    }

    [Fact]
    public async Task Upload_NotAnImage_IsRefused()
    {
        var (importer, _, _) = CreateImporter(upscaler: false);
        var subject = new ArtSubject("water", ArtKind.Icon, ["water"], "a water bottle");

        var ex = await Assert.ThrowsAsync<ArtImportException>(() =>
            importer.SaveSourceAsync(subject, new MemoryStream("<html>"u8.ToArray()), CancellationToken.None));

        Assert.Contains("not a PNG, JPEG or WebP", ex.Message);
        Assert.False(importer.HasSource(subject));
    }

    [Fact]
    public async Task Import_ToolFails_ReportsTheStepAndItsLastErrorLine()
    {
        var (importer, _, _) = CreateImporter(upscaler: false, exitCode: 1, stderr: "Traceback...\nValueError: no empty column near the middle");
        var subject = new ArtSubject("squat-x", ArtKind.ExercisePair, ["x"], "x", "a", "b");
        await importer.SaveSourceAsync(subject, new MemoryStream(TinyPng()), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<ArtImportException>(() => importer.ImportAsync(subject, CancellationToken.None));

        Assert.Equal("Preparing the picture (prepare_art.py) failed: ValueError: no empty column near the middle", ex.Message);
    }

    // A temp "repo": manifest with one mascot, an incoming folder, and a fake process runner
    // that writes a real PNG to every output path prepare_art.py would write.
    private (ArtImporter Importer, FakeProcessRunner Runner, string Manifest) CreateImporter(bool upscaler, int exitCode = 0, string stderr = "")
    {
        var manifestDir = Path.Combine(_root, "assets", "manifests");
        Directory.CreateDirectory(manifestDir);
        Directory.CreateDirectory(Path.Combine(_root, "assets", "mascot"));
        File.WriteAllBytes(Path.Combine(_root, "assets", "mascot", "mascot-idle.png"), TinyPng());
        var manifest = Path.Combine(manifestDir, "assets.json");
        File.WriteAllText(manifest, AssetCatalogEditor.Serialize(
        [
            new AssetMetadata("mascot-idle", "../mascot/mascot-idle.png", "mascot", ["mascot"], [SceneType.Hook], AssetOrientation.Square, "center"),
            new AssetMetadata("mascot-thinking", "../mascot/mascot-idle.png", "mascot", ["mascot"], [SceneType.Hook], AssetOrientation.Square, "center"),
            new AssetMetadata("mascot-happy", "../mascot/mascot-idle.png", "mascot", ["mascot"], [SceneType.Hook], AssetOrientation.Square, "center"),
            new AssetMetadata("mascot-tired", "../mascot/mascot-idle.png", "mascot", ["mascot"], [SceneType.Hook], AssetOrientation.Square, "center"),
        ]));
        var upscalerPath = Path.Combine(_root, "realesrgan.exe");
        if (upscaler)
        {
            File.WriteAllText(upscalerPath, "fake");
        }

        var runner = new FakeProcessRunner(spec =>
        {
            if (exitCode == 0)
            {
                foreach (var output in spec.Arguments.Where(a => a.EndsWith(".png", StringComparison.Ordinal)).Skip(1))
                {
                    File.WriteAllBytes(output, TinyPng());
                }
            }

            return new ProcessResult(exitCode, "", stderr, TimeSpan.FromSeconds(1));
        });
        var options = Options.Create(new ArtImportOptions
        {
            IncomingDirectory = Path.Combine(_root, "incoming"),
            WorkDirectory = Path.Combine(_root, "work"),
            UpscalerPath = upscalerPath,
            PythonPath = "python",
            PrepareScriptPath = "prepare_art.py",
        });
        var importer = new ArtImporter(runner, options, Options.Create(new AssetManifestOptions { ManifestPath = manifest }), NullLogger<ArtImporter>.Instance);
        return (importer, runner, manifest);
    }

    // Just the PNG signature and IHDR header - all the catalog validator reads (as in AssetToolingTests).
    private static byte[] TinyPng()
    {
        var bytes = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }.CopyTo(bytes, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), 1024);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20), 1024);
        return bytes;
    }
}
