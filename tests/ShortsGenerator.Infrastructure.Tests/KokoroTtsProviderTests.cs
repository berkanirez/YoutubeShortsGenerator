using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ShortsGenerator.Domain;
using ShortsGenerator.Infrastructure.Processes;
using ShortsGenerator.Infrastructure.Tts;

namespace ShortsGenerator.Infrastructure.Tests;

public sealed class KokoroTtsProviderTests : IDisposable
{
    private const string SuccessJson =
        """{"durationSeconds": 2.5, "sampleRate": 24000, "voice": "af_heart", "segments": [{"startSeconds": 0, "durationSeconds": 2.2}]}""";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "kokoro-tests-" + Guid.NewGuid());
    private readonly KokoroTtsOptions _options;

    public KokoroTtsProviderTests()
    {
        Directory.CreateDirectory(_directory);
        _options = new KokoroTtsOptions
        {
            PythonPath = Touch("python.exe"),
            ScriptPath = Touch("kokoro_tts.py"),
            ModelPath = Touch("kokoro-v1.0.onnx"),
            VoicesPath = Touch("voices-v1.0.bin"),
            Speed = 1.1,
        };
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string OutputPath => Path.Combine(_directory, "narration.wav");

    [Fact]
    public async Task SynthesizeAsync_Success_PassesSegmentsAsJsonOnStdinAndArgumentsSeparately()
    {
        const string json = """
            {"durationSeconds": 4.3, "sampleRate": 24000, "voice": "af_heart",
             "segments": [{"startSeconds": 0, "durationSeconds": 2.0}, {"startSeconds": 2.3, "durationSeconds": 1.7}]}
            """;
        var runner = Succeeding(json.ReplaceLineEndings(" "), wavSeconds: 4.3);
        var provider = CreateProvider(runner);

        var result = await provider.SynthesizeAsync(
            new TtsRequest(["Keep your \"core\" tight;", "avoid swinging."], OutputPath), CancellationToken.None);

        Assert.Equal(4.3, result.DurationSeconds);
        Assert.Equal(24000, result.SampleRate);
        Assert.Equal(OutputPath, result.AudioPath);
        Assert.Equal([new NarrationSegmentTiming(0, 2.0), new NarrationSegmentTiming(2.3, 1.7)], result.Segments);

        var spec = Assert.Single(runner.Calls);
        Assert.Equal(_options.PythonPath, spec.FileName);
        // Compared as parsed JSON, not as a string: how the serializer escapes the quotes
        // ("\"" vs """) is irrelevant - what Python reads back is what matters.
        using var stdin = System.Text.Json.JsonDocument.Parse(spec.StandardInput!);
        Assert.Equal(
            ["Keep your \"core\" tight;", "avoid swinging."],
            stdin.RootElement.GetProperty("segments").EnumerateArray().Select(e => e.GetString()));
        Assert.DoesNotContain(spec.Arguments, argument => argument.Contains("core"));
        Assert.Equal(
            [
                _options.ScriptPath,
                "--output", OutputPath,
                "--model", _options.ModelPath,
                "--voices", _options.VoicesPath,
                "--voice", "af_heart",
                "--speed", "1.1",
                "--lang", "en-us",
                "--pause", "0.3",
            ],
            spec.Arguments);
    }

    [Fact]
    public async Task SynthesizeAsync_WavLengthDisagreesWithReportedDuration_Throws()
    {
        var runner = Succeeding(wavSeconds: 1.0); // JSON says 2.5s

        var ex = await Assert.ThrowsAsync<TtsGenerationException>(() =>
            CreateProvider(runner).SynthesizeAsync(new TtsRequest(["Hello."], OutputPath), CancellationToken.None));

        Assert.Contains("reported", ex.Message);
    }

    [Fact]
    public async Task SynthesizeAsync_SuccessExitButNoFile_Throws()
    {
        var runner = FakeProcessRunner.Returning(0, SuccessJson);

        await Assert.ThrowsAsync<TtsGenerationException>(() =>
            CreateProvider(runner).SynthesizeAsync(new TtsRequest(["Hello."], OutputPath), CancellationToken.None));
    }

    [Fact]
    public async Task SynthesizeAsync_SegmentCountMismatch_Throws()
    {
        var runner = Succeeding(); // reports one segment

        await Assert.ThrowsAsync<TtsGenerationException>(() =>
            CreateProvider(runner).SynthesizeAsync(new TtsRequest(["One.", "Two."], OutputPath), CancellationToken.None));
    }

    [Fact]
    public async Task SynthesizeAsync_EmptySegment_IsRejectedBeforeStartingAProcess()
    {
        var runner = Succeeding();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            CreateProvider(runner).SynthesizeAsync(new TtsRequest(["One.", " "], OutputPath), CancellationToken.None));
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task SynthesizeAsync_TurkishCulture_StillFormatsSpeedWithDot()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
        try
        {
            var runner = Succeeding();
            await CreateProvider(runner).SynthesizeAsync(new TtsRequest(["Hello there."], OutputPath), CancellationToken.None);

            var arguments = runner.Calls[0].Arguments;
            Assert.Equal("1.1", arguments[arguments.ToList().IndexOf("--speed") + 1]);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task SynthesizeAsync_IgnoresLibraryNoiseBeforeTheJsonLine()
    {
        var runner = Succeeding(stdoutPrefix: "some onnxruntime warning\n", stdoutSuffix: "\n");

        var result = await CreateProvider(runner).SynthesizeAsync(new TtsRequest(["Hello."], OutputPath), CancellationToken.None);

        Assert.Equal(2.5, result.DurationSeconds);
    }

    [Fact]
    public async Task SynthesizeAsync_RequestVoiceOverridesConfiguredVoice()
    {
        var runner = Succeeding();

        await CreateProvider(runner).SynthesizeAsync(new TtsRequest(["Hello."], OutputPath, "am_michael"), CancellationToken.None);

        var arguments = runner.Calls[0].Arguments.ToList();
        Assert.Equal("am_michael", arguments[arguments.IndexOf("--voice") + 1]);
    }

    [Theory]
    [InlineData("af_heart --model evil.onnx")]
    [InlineData("../../secrets")]
    [InlineData("AF_HEART")]
    public async Task SynthesizeAsync_InvalidVoiceId_IsRejectedBeforeStartingAProcess(string voice)
    {
        var runner = Succeeding();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            CreateProvider(runner).SynthesizeAsync(new TtsRequest(["Hello."], OutputPath, voice), CancellationToken.None));
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task SynthesizeAsync_RelativeOutputPath_IsRejected()
    {
        var runner = Succeeding();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            CreateProvider(runner).SynthesizeAsync(new TtsRequest(["Hello."], "narration.wav"), CancellationToken.None));
    }

    [Fact]
    public async Task SynthesizeAsync_MissingModelFile_ThrowsUnavailableWithSetupHint_WithoutStartingAProcess()
    {
        File.Delete(_options.ModelPath);
        var runner = Succeeding();

        var ex = await Assert.ThrowsAsync<TtsUnavailableException>(() =>
            CreateProvider(runner).SynthesizeAsync(new TtsRequest(["Hello."], OutputPath), CancellationToken.None));

        Assert.Contains("Kokoro model", ex.Message);
        Assert.Contains("setup.ps1", ex.Message);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task SynthesizeAsync_ExitCode3_ThrowsUnavailable()
    {
        var runner = FakeProcessRunner.Returning(3, stderr: "Model file not found at 'x'.");

        var ex = await Assert.ThrowsAsync<TtsUnavailableException>(() =>
            CreateProvider(runner).SynthesizeAsync(new TtsRequest(["Hello."], OutputPath), CancellationToken.None));

        Assert.Contains("Model file not found", ex.Message);
    }

    [Fact]
    public async Task SynthesizeAsync_EngineCrash_ThrowsGenerationErrorWithLastStderrLine()
    {
        var runner = FakeProcessRunner.Returning(4, stderr: "Traceback...\nKokoro synthesis failed: ValueError: bad phonemes\n");

        var ex = await Assert.ThrowsAsync<TtsGenerationException>(() =>
            CreateProvider(runner).SynthesizeAsync(new TtsRequest(["Hello."], OutputPath), CancellationToken.None));

        Assert.Contains("bad phonemes", ex.Message);
        Assert.Contains("exit code 4", ex.Message);
    }

    [Fact]
    public async Task SynthesizeAsync_SuccessExitButGarbageOutput_ThrowsGenerationError()
    {
        var runner = FakeProcessRunner.Returning(0, "not json");

        await Assert.ThrowsAsync<TtsGenerationException>(() =>
            CreateProvider(runner).SynthesizeAsync(new TtsRequest(["Hello."], OutputPath), CancellationToken.None));
    }

    [Fact]
    public async Task SynthesizeAsync_PythonCannotStart_ThrowsUnavailable()
    {
        var runner = FakeProcessRunner.Throwing(new ProcessLaunchException("file not found"));

        await Assert.ThrowsAsync<TtsUnavailableException>(() =>
            CreateProvider(runner).SynthesizeAsync(new TtsRequest(["Hello."], OutputPath), CancellationToken.None));
    }

    [Fact]
    public async Task SynthesizeAsync_Timeout_ThrowsGenerationErrorMentioningTheSetting()
    {
        var runner = FakeProcessRunner.Throwing(new TimeoutException());

        var ex = await Assert.ThrowsAsync<TtsGenerationException>(() =>
            CreateProvider(runner).SynthesizeAsync(new TtsRequest(["Hello."], OutputPath), CancellationToken.None));

        Assert.Contains("TimeoutSeconds", ex.Message);
    }

    [Fact]
    public async Task CheckAvailabilityAsync_ListsEveryMissingPiece()
    {
        File.Delete(_options.PythonPath);
        File.Delete(_options.VoicesPath);

        var availability = await CreateProvider(FakeProcessRunner.Returning(0)).CheckAvailabilityAsync(CancellationToken.None);

        Assert.False(availability.IsReady);
        Assert.Equal(2, availability.Problems.Count);
    }

    // Behaves like the real script on success: writes a real .wav to the --output path,
    // then prints the JSON line.
    private static FakeProcessRunner Succeeding(
        string json = SuccessJson, double wavSeconds = 2.5, string stdoutPrefix = "", string stdoutSuffix = "") =>
        new(spec =>
        {
            var outputPath = spec.Arguments[spec.Arguments.ToList().IndexOf("--output") + 1];
            TestWav.Write(outputPath, wavSeconds);
            return new ProcessResult(0, stdoutPrefix + json + stdoutSuffix, string.Empty, TimeSpan.FromSeconds(1));
        });

    private KokoroTtsProvider CreateProvider(IProcessRunner runner) =>
        new(runner, Options.Create(_options), NullLogger<KokoroTtsProvider>.Instance);

    private string Touch(string fileName)
    {
        var path = Path.Combine(_directory, fileName);
        File.WriteAllText(path, string.Empty);
        return path;
    }
}
