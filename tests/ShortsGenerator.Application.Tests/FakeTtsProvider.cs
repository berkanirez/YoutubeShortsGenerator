using ShortsGenerator.Domain;

namespace ShortsGenerator.Application.Tests;

// Pretends every segment takes SegmentSeconds to say, followed by PauseSeconds of silence -
// the same layout the real Kokoro script produces.
internal sealed class FakeTtsProvider : ITtsProvider
{
    public bool IsReady { get; init; } = true;
    public Exception? SynthesisError { get; init; }
    public double SegmentSeconds { get; init; } = 3.0;
    public double PauseSeconds { get; init; } = 0.3;
    public bool WriteOutputFile { get; init; }
    public List<TtsRequest> Requests { get; } = [];

    public Task<TtsAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken) =>
        Task.FromResult(IsReady
            ? new TtsAvailability(true, "fake", [])
            : new TtsAvailability(false, "fake", ["fake engine missing"], "Run install-fake.ps1"));

    public Task<TtsResult> SynthesizeAsync(TtsRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (SynthesisError is not null)
        {
            throw SynthesisError;
        }

        if (WriteOutputFile)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(request.OutputPath)!);
            File.WriteAllBytes(request.OutputPath, [0]);
        }

        var step = SegmentSeconds + PauseSeconds;
        var segments = request.Segments
            .Select((_, i) => new NarrationSegmentTiming(i * step, SegmentSeconds))
            .ToList();
        return Task.FromResult(new TtsResult(
            request.OutputPath, segments.Count * step, 24000, request.Voice ?? "fake_voice", segments));
    }
}

internal sealed class FakeJobOutputLocator : IJobOutputLocator
{
    public string GetJobDirectory(Guid jobId) => Path.Combine(Path.GetTempPath(), "jobs", jobId.ToString());
}
