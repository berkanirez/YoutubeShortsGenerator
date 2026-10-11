using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShortsGenerator.Domain;
using ShortsGenerator.Infrastructure.Processes;

namespace ShortsGenerator.Infrastructure.Rendering;

// Adapter: IRenderer -> the Remotion CLI in renderer/. Remotion's headless Chrome can only
// read files from renderer/public/ (Day 8), so a render is:
//   1. sync the repo's assets/ into public/ (renderer/scripts/sync-public-assets.mjs),
//   2. stage this job's plan.json + narration.wav into public/jobs/<job-id>/,
//   3. run `remotion render` with a props *file* pointing at the staged copies,
//   4. move the finished video into place and remove the staging folder.
public sealed partial class RemotionRenderer : IRenderer
{
    private const int ErrorTailLines = 15;
    private static readonly TimeSpan HelperTimeout = TimeSpan.FromMinutes(1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IProcessRunner _processRunner;
    private readonly RemotionOptions _options;
    private readonly ILogger<RemotionRenderer> _logger;

    public RemotionRenderer(IProcessRunner processRunner, IOptions<RemotionOptions> options, ILogger<RemotionRenderer> logger)
    {
        _processRunner = processRunner;
        _options = options.Value;
        _logger = logger;
    }

    private string CliPath => Path.Combine(_options.RendererDirectory, "node_modules", "@remotion", "cli", "remotion-cli.js");
    private string SyncScriptPath => Path.Combine(_options.RendererDirectory, "scripts", "sync-public-assets.mjs");

    public async Task<RendererAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        if (!Directory.Exists(_options.RendererDirectory))
        {
            problems.Add($"Renderer folder not found at '{_options.RendererDirectory}'");
        }
        else
        {
            if (!File.Exists(CliPath))
            {
                problems.Add("Remotion is not installed (renderer/node_modules is missing)");
            }

            if (!File.Exists(Path.Combine(_options.RendererDirectory, _options.EntryPoint)))
            {
                problems.Add($"Renderer entry point '{_options.EntryPoint}' not found");
            }
        }

        try
        {
            var node = await _processRunner.RunAsync(
                new ProcessSpec(_options.NodePath, ["--version"], null, TimeSpan.FromSeconds(15)), cancellationToken);
            if (node.ExitCode != 0)
            {
                problems.Add($"'{_options.NodePath} --version' failed");
            }
        }
        catch (Exception ex) when (ex is ProcessLaunchException or TimeoutException)
        {
            problems.Add($"Node.js could not be started ('{_options.NodePath}'): {ex.Message}");
        }

        return new RendererAvailability(
            problems.Count == 0,
            "remotion",
            problems,
            problems.Count == 0 ? null : "Install Node.js, then run: cd renderer && npm install");
    }

    public async Task<RenderResult> RenderAsync(RenderRequest request, CancellationToken cancellationToken)
    {
        ValidateRequest(request);

        var fingerprintPath = Path.ChangeExtension(request.OutputPath, ".inputs.sha256");
        var fingerprint = _options.SkipUnchangedRenders ? await FingerprintAsync(request, cancellationToken) : null;
        if (fingerprint is not null && File.Exists(request.OutputPath) && File.Exists(fingerprintPath) &&
            (await File.ReadAllTextAsync(fingerprintPath, cancellationToken)).Trim() == fingerprint)
        {
            // Touched so the video doesn't look older than plan.json (the dashboard's "outdated"
            // check, Day 22) after e.g. an edit that was reverted.
            File.SetLastWriteTimeUtc(request.OutputPath, DateTime.UtcNow);
            var existing = new FileInfo(request.OutputPath).Length;
            _logger.LogInformation(
                "Job {JobId}: plan, narration, renderer and assets are unchanged since the last render; reusing {OutputPath}",
                request.JobId, request.OutputPath);
            return new RenderResult(request.OutputPath, existing, TimeSpan.Zero, Skipped: true);
        }

        var availability = await CheckAvailabilityAsync(cancellationToken);
        if (!availability.IsReady)
        {
            throw new RendererUnavailableException(
                $"Renderer is not set up: {string.Join("; ", availability.Problems)}. {availability.SetupHint}");
        }

        var stopwatch = Stopwatch.StartNew();
        var stagingDirectory = Path.Combine(_options.RendererDirectory, "public", "jobs", request.JobId.ToString());
        var temporaryOutput = Path.ChangeExtension(request.OutputPath, ".rendering.mp4");
        try
        {
            await RunHelperAsync([SyncScriptPath], "Syncing assets into renderer/public", cancellationToken);
            var propsPath = StageJobFiles(request, stagingDirectory);

            var spec = new ProcessSpec(
                _options.NodePath,
                [
                    CliPath, "render", _options.EntryPoint, _options.CompositionId, temporaryOutput,
                    "--props", propsPath,
                    "--log", "warn",
                    .. (_options.Concurrency is { } concurrency
                        ? new[] { "--concurrency", concurrency.ToString(System.Globalization.CultureInfo.InvariantCulture) }
                        : []),
                ],
                StandardInput: null,
                Timeout: TimeSpan.FromSeconds(_options.TimeoutSeconds),
                WorkingDirectory: _options.RendererDirectory);

            var result = await RunAsync(spec, cancellationToken);
            if (result.ExitCode != 0)
            {
                _logger.LogError(
                    "Remotion render for job {JobId} exited with {ExitCode}. stderr: {StandardError} stdout: {StandardOutput}",
                    request.JobId, result.ExitCode, result.StandardError, result.StandardOutput);
                throw new RenderFailedException(
                    $"Remotion render failed (exit code {result.ExitCode}): {SummarizeFailure(result)}");
            }

            if (!File.Exists(temporaryOutput) || new FileInfo(temporaryOutput).Length == 0)
            {
                throw new RenderFailedException("Remotion reported success but produced no video file.");
            }

            File.Move(temporaryOutput, request.OutputPath, overwrite: true);

            // The thumbnail next to the video (cover.png). Best effort: a failed cover never
            // fails a finished video.
            var coverPath = Path.Combine(Path.GetDirectoryName(request.OutputPath)!, "cover.png");
            var cover = await RunAsync(
                new ProcessSpec(
                    _options.NodePath,
                    [CliPath, "still", _options.EntryPoint, "ShortCover", coverPath, "--props", propsPath, "--log", "warn"],
                    StandardInput: null,
                    Timeout: TimeSpan.FromSeconds(120),
                    WorkingDirectory: _options.RendererDirectory),
                cancellationToken);
            if (cover.ExitCode != 0)
            {
                _logger.LogWarning("Job {JobId}: the cover image could not be rendered: {Reason}", request.JobId, SummarizeFailure(cover));
            }

            if (fingerprint is not null)
            {
                await File.WriteAllTextAsync(fingerprintPath, fingerprint, cancellationToken);
            }

            var size = new FileInfo(request.OutputPath).Length;
            _logger.LogInformation(
                "Rendered job {JobId} to {OutputPath} ({SizeKb} kB) in {ElapsedSeconds:0.0}s",
                request.JobId, request.OutputPath, size / 1024, stopwatch.Elapsed.TotalSeconds);

            return new RenderResult(request.OutputPath, size, stopwatch.Elapsed);
        }
        finally
        {
            TryDelete(() => Directory.Delete(stagingDirectory, recursive: true), stagingDirectory);
            TryDelete(() => File.Delete(temporaryOutput), temporaryOutput);
        }
    }

    // Everything a render's pixels and sound depend on: this job's plan and narration, the
    // renderer's own code and lock file, and the shared assets (drawings, music, sfx). Any
    // byte changing in any of them changes the hash. A few MB of hashing: milliseconds.
    private async Task<string> FingerprintAsync(RenderRequest request, CancellationToken cancellationToken)
    {
        var files = new List<(string Name, string Path)> { ("job/plan.json", request.PlanPath) };
        if (request.NarrationPath is not null)
        {
            files.Add(("job/narration.wav", request.NarrationPath));
        }

        AddTree(files, "renderer/src", Path.Combine(_options.RendererDirectory, "src"));
        AddTree(files, "assets", Path.Combine(_options.RendererDirectory, "..", "assets"));
        var lockFile = Path.Combine(_options.RendererDirectory, "package-lock.json");
        if (File.Exists(lockFile))
        {
            files.Add(("renderer/package-lock.json", lockFile));
        }

        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        foreach (var (name, path) in files.OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            hash.AppendData(System.Text.Encoding.UTF8.GetBytes(name + "\0"));
            await using var stream = File.OpenRead(path);
            hash.AppendData(await System.Security.Cryptography.SHA256.HashDataAsync(stream, cancellationToken));
        }

        return Convert.ToHexString(hash.GetHashAndReset());

        static void AddTree(List<(string, string)> files, string prefix, string directory)
        {
            if (!Directory.Exists(directory))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                files.Add(($"{prefix}/{Path.GetRelativePath(directory, file).Replace('\\', '/')}", file));
            }
        }
    }

    private static void ValidateRequest(RenderRequest request)
    {
        if (!Path.IsPathFullyQualified(request.PlanPath) || !File.Exists(request.PlanPath))
        {
            throw new ArgumentException($"Plan file '{request.PlanPath}' does not exist.", nameof(request));
        }

        if (request.NarrationPath is not null &&
            (!Path.IsPathFullyQualified(request.NarrationPath) || !File.Exists(request.NarrationPath)))
        {
            throw new ArgumentException($"Narration file '{request.NarrationPath}' does not exist.", nameof(request));
        }

        if (!Path.IsPathFullyQualified(request.OutputPath) ||
            !request.OutputPath.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Output path must be an absolute .mp4 path.", nameof(request));
        }
    }

    // Copies are named by convention, never by anything taken from the plan, and the folder
    // name is the job's Guid - nothing a plan or LLM produces ever becomes a path here.
    private static string StageJobFiles(RenderRequest request, string stagingDirectory)
    {
        Directory.CreateDirectory(stagingDirectory);
        var publicPrefix = $"jobs/{request.JobId}";

        File.Copy(request.PlanPath, Path.Combine(stagingDirectory, "plan.json"), overwrite: true);
        string? narrationPublicPath = null;
        if (request.NarrationPath is not null)
        {
            File.Copy(request.NarrationPath, Path.Combine(stagingDirectory, "narration.wav"), overwrite: true);
            narrationPublicPath = $"{publicPrefix}/narration.wav";
        }

        // Passed as a file instead of inline JSON on the command line: no quoting rules to
        // get right, no command-line length limit.
        var propsPath = Path.Combine(stagingDirectory, "render-props.json");
        File.WriteAllText(
            propsPath,
            JsonSerializer.Serialize(new { planPath = $"{publicPrefix}/plan.json", narrationPath = narrationPublicPath }, JsonOptions));
        return propsPath;
    }

    private async Task RunHelperAsync(IReadOnlyList<string> arguments, string description, CancellationToken cancellationToken)
    {
        var result = await RunAsync(
            new ProcessSpec(_options.NodePath, arguments, null, HelperTimeout, _options.RendererDirectory),
            cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new RenderFailedException($"{description} failed (exit code {result.ExitCode}): {SummarizeFailure(result)}");
        }
    }

    private async Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken)
    {
        try
        {
            return await _processRunner.RunAsync(spec, cancellationToken);
        }
        catch (ProcessLaunchException ex)
        {
            throw new RendererUnavailableException($"Could not start Node.js: {ex.Message}", ex);
        }
        catch (TimeoutException ex)
        {
            throw new RenderFailedException(
                $"Rendering did not finish within {spec.Timeout.TotalSeconds:0}s and was stopped. " +
                "Raise Remotion:TimeoutSeconds if your machine needs longer.",
                ex);
        }
    }

    // Picks the line that actually explains the failure. Found with a real broken plan:
    // Remotion's stderr was a 15-line puppeteer "Was not able to close puppeteer page"
    // stack trace (cleanup noise) followed by the real " Error  Error: Invalid plan JSON..."
    // line, so a blind "last N lines" summary was mostly noise. Falls back to the tail
    // when no recognizable error line exists.
    private static string SummarizeFailure(ProcessResult result)
    {
        var lines = (result.StandardError + "\n" + result.StandardOutput)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var errorLine = lines.LastOrDefault(line =>
            ErrorLinePattern().IsMatch(line) &&
            !line.StartsWith("at ", StringComparison.Ordinal) &&
            !line.Contains("puppeteer", StringComparison.OrdinalIgnoreCase) &&
            !line.Contains("ProtocolError", StringComparison.Ordinal));
        if (errorLine is not null)
        {
            return ErrorLinePattern().Match(errorLine).Groups["message"].Value.Trim();
        }

        return lines.Length == 0 ? "(no output)" : string.Join(" | ", lines.TakeLast(ErrorTailLines));
    }

    // "Error: msg", "TypeError: msg", or Remotion's labelled " Error  Error: msg".
    [GeneratedRegex(@"(?<message>\b[A-Za-z]*Error: .+)$")]
    private static partial Regex ErrorLinePattern();

    private void TryDelete(Action delete, string path)
    {
        try
        {
            delete();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not clean up {Path}", path);
        }
    }
}
