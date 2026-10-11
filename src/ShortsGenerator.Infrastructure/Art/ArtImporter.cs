using System.Globalization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShortsGenerator.Domain;
using ShortsGenerator.Infrastructure.Assets;
using ShortsGenerator.Infrastructure.Processes;

namespace ShortsGenerator.Infrastructure.Art;

// The same steps as the hand workflow in docs/ASSETS.md, run by the API:
//   assets/incoming/<file>  -> Real-ESRGAN 4x (if installed) -> prepare_art.py
//   (--pair splits an exercise pair, --keep-holes keeps an icon's white parts)
//   -> AssetCatalogEditor.Add (validated; rolled back on error).
// Every file name comes from a validated subject id; tools get their arguments one by one.
public sealed class ArtImporter : IArtImporter
{
    private readonly IProcessRunner _processRunner;
    private readonly ArtImportOptions _options;
    private readonly string _manifestPath;
    private readonly ILogger<ArtImporter> _logger;

    public ArtImporter(
        IProcessRunner processRunner,
        IOptions<ArtImportOptions> options,
        IOptions<AssetManifestOptions> manifest,
        ILogger<ArtImporter> logger)
    {
        _processRunner = processRunner;
        _options = options.Value;
        _manifestPath = manifest.Value.ManifestPath;
        _logger = logger;
    }

    private string SourcePath(ArtSubject subject) => Path.Combine(_options.IncomingDirectory, subject.IncomingFileName);

    public bool HasSource(ArtSubject subject) => File.Exists(SourcePath(subject));

    public async Task SaveSourceAsync(ArtSubject subject, Stream content, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length > _options.MaxUploadBytes)
        {
            throw new ArtImportException($"The image is {buffer.Length / 1024 / 1024} MB; at most {_options.MaxUploadBytes / 1024 / 1024} MB.");
        }

        if (!LooksLikeImage(buffer.GetBuffer().AsSpan(0, (int)Math.Min(buffer.Length, 16))))
        {
            throw new ArtImportException("That file is not a PNG, JPEG or WebP image.");
        }

        Directory.CreateDirectory(_options.IncomingDirectory);
        await File.WriteAllBytesAsync(SourcePath(subject), buffer.ToArray(), cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ImportAsync(ArtSubject subject, CancellationToken cancellationToken)
    {
        var editor = new AssetCatalogEditor(_manifestPath);
        var existing = editor.ExistingIds();
        if (subject.AssetIds.All(existing.Contains))
        {
            return [];
        }

        var source = SourcePath(subject);
        if (!File.Exists(source))
        {
            throw new ArtImportException($"'{subject.IncomingFileName}' is not in {_options.IncomingDirectory}.");
        }

        var work = Path.Combine(_options.WorkDirectory, subject.Id);
        if (Directory.Exists(work))
        {
            Directory.Delete(work, recursive: true);
        }

        Directory.CreateDirectory(work);

        var input = source;
        if (File.Exists(_options.UpscalerPath))
        {
            input = Path.Combine(work, "hd.png");
            await RunAsync(
                "Upscaling (Real-ESRGAN)",
                _options.UpscalerPath,
                ["-i", source, "-o", input, "-n", _options.UpscalerModel],
                cancellationToken);
        }
        else
        {
            _logger.LogWarning("Real-ESRGAN not found at {Path}; importing {File} without HD upscaling", _options.UpscalerPath, subject.IncomingFileName);
        }

        var outputs = subject.AssetIds.Select(id => Path.Combine(work, $"{id}.png")).ToList();
        var arguments = new List<string> { _options.PrepareScriptPath, "--size", _options.Size.ToString(CultureInfo.InvariantCulture) };
        if (subject.Kind == ArtKind.ExercisePair)
        {
            arguments.Add("--pair");
        }
        else if (subject.Kind is ArtKind.Icon or ArtKind.Equipment)
        {
            arguments.Add("--keep-holes");
        }

        arguments.Add(input);
        arguments.AddRange(outputs);
        await RunAsync("Preparing the picture (prepare_art.py)", _options.PythonPath, arguments, cancellationToken);

        var added = new List<string>();
        foreach (var (id, file) in subject.AssetIds.Zip(outputs))
        {
            if (existing.Contains(id))
            {
                continue;
            }

            var report = editor.Add(new NewAsset(id, file, subject.Category, subject.Tags, subject.SceneTypes, Anchor: subject.Anchor));
            if (!report.IsValid)
            {
                throw new ArtImportException($"'{id}' was not added to the catalog: {string.Join("; ", report.Errors)}");
            }

            added.Add(id);
        }

        return added;
    }

    private async Task RunAsync(string step, string fileName, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        ProcessResult result;
        try
        {
            result = await _processRunner.RunAsync(
                new ProcessSpec(fileName, arguments, null, TimeSpan.FromSeconds(_options.TimeoutSeconds)), cancellationToken);
        }
        catch (ProcessLaunchException ex)
        {
            throw new ArtImportException($"{step}: could not start '{fileName}' ({ex.Message}). See docs/ASSETS.md for the image tools setup.", ex);
        }
        catch (TimeoutException ex)
        {
            throw new ArtImportException($"{step} took longer than {_options.TimeoutSeconds}s and was stopped.", ex);
        }

        if (result.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(result.StandardError) ? result.StandardOutput : result.StandardError;
            throw new ArtImportException($"{step} failed: {LastLine(detail)}");
        }
    }

    private static string LastLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? "no output";

    private static bool LooksLikeImage(ReadOnlySpan<byte> head) =>
        head.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47 }) ||
        head.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF }) ||
        (head.Length >= 12 && head[..4].SequenceEqual("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8));
}
