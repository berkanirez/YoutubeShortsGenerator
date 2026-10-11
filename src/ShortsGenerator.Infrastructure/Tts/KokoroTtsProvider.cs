using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShortsGenerator.Domain;
using ShortsGenerator.Infrastructure.Media;
using ShortsGenerator.Infrastructure.Processes;

namespace ShortsGenerator.Infrastructure.Tts;

// Adapter: ITtsProvider (what the app needs) -> tools/tts/kokoro_tts.py (how Kokoro is
// actually run). Exit codes and the stdout JSON shape are the contract documented at the
// top of kokoro_tts.py; they have to change together.
public sealed partial class KokoroTtsProvider : ITtsProvider
{
    private const int ExitBadInput = 2;
    private const int ExitModelMissing = 3;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IProcessRunner _processRunner;
    private readonly KokoroTtsOptions _options;
    private readonly ILogger<KokoroTtsProvider> _logger;

    public KokoroTtsProvider(
        IProcessRunner processRunner, IOptions<KokoroTtsOptions> options, ILogger<KokoroTtsProvider> logger)
    {
        _processRunner = processRunner;
        _options = options.Value;
        _logger = logger;
    }

    public Task<TtsAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        AddIfMissing(problems, "Python virtual environment", _options.PythonPath);
        AddIfMissing(problems, "Kokoro script", _options.ScriptPath);
        AddIfMissing(problems, "Kokoro model", _options.ModelPath);
        AddIfMissing(problems, "Kokoro voices", _options.VoicesPath);

        return Task.FromResult(new TtsAvailability(
            problems.Count == 0, "kokoro", problems, problems.Count == 0 ? null : SetupHint));
    }

    private const string SetupHint = "Run: powershell -ExecutionPolicy Bypass -File tools/tts/setup.ps1";

    public async Task<TtsResult> SynthesizeAsync(TtsRequest request, CancellationToken cancellationToken)
    {
        if (request.Segments.Count == 0 || request.Segments.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("At least one segment is required, and no segment may be empty.", nameof(request));
        }

        if (!Path.IsPathFullyQualified(request.OutputPath) ||
            !request.OutputPath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Output path must be an absolute .wav path.", nameof(request));
        }

        var voice = request.Voice ?? _options.Voice;
        // Defense in depth: ArgumentList already prevents injection, but a voice id is
        // only ever lowercase letters + underscore, so anything else is rejected outright.
        if (!VoiceIdPattern().IsMatch(voice))
        {
            throw new ArgumentException($"'{voice}' is not a valid Kokoro voice id.", nameof(request));
        }

        var availability = await CheckAvailabilityAsync(cancellationToken);
        if (!availability.IsReady)
        {
            throw new TtsUnavailableException(
                $"Local TTS (Kokoro) is not set up: {string.Join("; ", availability.Problems)}. {SetupHint}");
        }

        var spec = BuildProcessSpec(StdinSegments(request), request.OutputPath, voice);
        ProcessResult result;
        try
        {
            result = await _processRunner.RunAsync(spec, cancellationToken);
        }
        catch (ProcessLaunchException ex)
        {
            throw new TtsUnavailableException($"Could not start the local TTS engine: {ex.Message}", ex);
        }
        catch (TimeoutException ex)
        {
            throw new TtsGenerationException(
                $"Local TTS did not finish within {_options.TimeoutSeconds}s. Try a shorter narration or raise KokoroTts:TimeoutSeconds.",
                ex);
        }

        var reason = LastLine(result.StandardError);
        switch (result.ExitCode)
        {
            case 0:
                break;
            case ExitModelMissing:
                throw new TtsUnavailableException($"Local TTS is not set up: {reason}");
            case ExitBadInput:
                throw new TtsGenerationException($"Local TTS rejected the request: {reason}");
            default:
                _logger.LogError(
                    "Kokoro exited with code {ExitCode}. stderr: {StandardError}", result.ExitCode, result.StandardError);
                throw new TtsGenerationException($"Local TTS failed (exit code {result.ExitCode}): {reason}");
        }

        var output = ParseOutput(result.StandardOutput, request.Segments.Count);
        VerifyAudioFile(request.OutputPath, output.DurationSeconds);
        _logger.LogInformation(
            "Kokoro synthesized {DurationSeconds:0.00}s of audio ({SegmentCount} segments) with voice {Voice} in {ElapsedSeconds:0.0}s",
            output.DurationSeconds, output.Segments.Count, output.Voice, result.Elapsed.TotalSeconds);

        return new TtsResult(
            request.OutputPath,
            output.DurationSeconds,
            output.SampleRate,
            output.Voice,
            output.Segments.Select(s => new NarrationSegmentTiming(s.StartSeconds, s.DurationSeconds)).ToList());
    }

    // The script's JSON says what it *meant* to write; the file header says what's actually
    // on disk. Every later timing decision (scene lengths, captions, video length) rests on
    // this duration, so a mismatch is treated as a failed synthesis, not trusted silently.
    private static void VerifyAudioFile(string path, double reportedDurationSeconds)
    {
        if (!File.Exists(path))
        {
            throw new TtsGenerationException($"Local TTS reported success but '{path}' was not created.");
        }

        WavInfo info;
        try
        {
            info = WavFileReader.Read(path);
        }
        catch (InvalidDataException ex)
        {
            throw new TtsGenerationException($"Local TTS wrote an unreadable audio file: {ex.Message}", ex);
        }

        if (Math.Abs(info.DurationSeconds - reportedDurationSeconds) > 0.05)
        {
            throw new TtsGenerationException(
                $"Audio file is {info.DurationSeconds:0.000}s long but the TTS engine reported {reportedDurationSeconds:0.000}s.");
        }
    }

    // Plain strings, or - when the scenes are known and the delivery is expressive - one
    // object per scene with its parts (see tools/tts/kokoro_tts.py).
    private IReadOnlyList<object> StdinSegments(TtsRequest request)
    {
        if (!_options.Expressive || request.Scenes is null || request.Scenes.Count != request.Segments.Count)
        {
            return request.Segments.Cast<object>().ToList();
        }

        return SpeechDelivery.Plan(request.Scenes, _options.Emphasis)
            .Select(s => (object)new
            {
                parts = s.Parts.Select(p => new { text = p.Text, speed = p.Speed, gain = p.Gain, pauseAfter = p.PauseAfter }).ToList(),
                pauseAfter = s.PauseAfter,
            })
            .ToList();
    }

    private ProcessSpec BuildProcessSpec(IReadOnlyList<object> segments, string outputPath, string voice) =>
        new(
            _options.PythonPath,
            [
                _options.ScriptPath,
                "--output", outputPath,
                "--model", _options.ModelPath,
                "--voices", _options.VoicesPath,
                "--voice", voice,
                // InvariantCulture: on a Turkish-locale machine 1.1 would otherwise become "1,1".
                "--speed", _options.Speed.ToString(CultureInfo.InvariantCulture),
                "--lang", _options.Language,
                "--pause", _options.PauseSeconds.ToString(CultureInfo.InvariantCulture),
            ],
            StandardInput: JsonSerializer.Serialize(new { segments }, JsonOptions),
            Timeout: TimeSpan.FromSeconds(_options.TimeoutSeconds));

    private static ParsedOutput ParseOutput(string standardOutput, int expectedSegments)
    {
        // The script prints exactly one JSON line on success; the engine's own libraries
        // may print warnings first, so only the last non-empty line is the contract.
        var line = LastLine(standardOutput);
        try
        {
            var output = JsonSerializer.Deserialize<KokoroOutput>(line, JsonOptions);
            if (output is null || output.DurationSeconds <= 0 || output.SampleRate <= 0 ||
                output.Segments is null || output.Segments.Count != expectedSegments)
            {
                throw new TtsGenerationException($"Local TTS returned an unexpected result: '{line}'.");
            }

            return new ParsedOutput(output.DurationSeconds, output.SampleRate, output.Voice, output.Segments);
        }
        catch (JsonException ex)
        {
            throw new TtsGenerationException($"Local TTS returned output that isn't valid JSON: '{line}'.", ex);
        }
    }

    private static string LastLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault()
        ?? "(no output)";

    private static void AddIfMissing(List<string> problems, string label, string path)
    {
        if (!File.Exists(path))
        {
            problems.Add($"{label} not found at '{path}'");
        }
    }

    [GeneratedRegex("^[a-z]{1,4}_[a-z]{1,32}$")]
    private static partial Regex VoiceIdPattern();

    private sealed record KokoroOutput(
        double DurationSeconds, int SampleRate, string Voice, IReadOnlyList<KokoroSegment>? Segments);

    private sealed record KokoroSegment(double StartSeconds, double DurationSeconds);

    // KokoroOutput after validation: what the wire format makes optional is guaranteed here.
    private sealed record ParsedOutput(
        double DurationSeconds, int SampleRate, string Voice, IReadOnlyList<KokoroSegment> Segments);
}
