using ShortsGenerator.Domain;

namespace ShortsGenerator.Application;

public sealed record NarrationOutcome(TtsResult Audio, VideoPlan TimedPlan);

public sealed class NarrationService
{
    public const string NarrationFileName = "narration.wav";

    private readonly ITtsProvider _ttsProvider;
    private readonly IVideoJobRepository _repository;
    private readonly IVideoPlanStore _planStore;
    private readonly IJobOutputLocator _outputLocator;
    private readonly TimeProvider _timeProvider;

    public NarrationService(
        ITtsProvider ttsProvider,
        IVideoJobRepository repository,
        IVideoPlanStore planStore,
        IJobOutputLocator outputLocator,
        TimeProvider timeProvider)
    {
        _ttsProvider = ttsProvider;
        _repository = repository;
        _planStore = planStore;
        _outputLocator = outputLocator;
        _timeProvider = timeProvider;
    }

    public async Task<NarrationOutcome> GenerateNarrationAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await _repository.GetByIdAsync(jobId, cancellationToken)
            ?? throw new KeyNotFoundException($"Video job '{jobId}' was not found.");

        // Narrating is allowed again so a narration can be regenerated (e.g. after changing
        // the configured voice) without starting the whole job over; Completed too, after a
        // scene's narration text was edited (Day 23).
        if (job.Status is not (VideoJobStatus.Planning or VideoJobStatus.Narrating or VideoJobStatus.Completed))
        {
            throw new InvalidOperationException(
                $"Video job '{jobId}' is '{job.Status}'; narration can only be generated while it is Planning, Narrating or Completed.");
        }

        var plan = await _planStore.LoadAsync(jobId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Video job '{jobId}' has no scene plan yet. Generate it (POST /api/videos/{{id}}/plan) before narration.");

        // Checked before touching the job: a missing TTS install is a one-time machine
        // setup problem, not this job's fault, so it shouldn't burn the job as Failed.
        var availability = await _ttsProvider.CheckAvailabilityAsync(cancellationToken);
        if (!availability.IsReady)
        {
            throw new TtsUnavailableException(
                $"Local TTS ({availability.Engine}) is not ready: {string.Join("; ", availability.Problems)}." +
                (availability.SetupHint is null ? string.Empty : $" {availability.SetupHint}"));
        }

        if (job.Status is VideoJobStatus.Planning or VideoJobStatus.Completed)
        {
            job.MarkNarrating(_timeProvider.GetUtcNow());
            await _repository.UpdateAsync(job, cancellationToken);
        }

        // One segment per narrated scene, in scene order. Together they are exactly the
        // plan's NarrationText (VideoPlanValidator guarantees it), but split at the scene
        // boundaries so the audio can tell us where each scene's speech starts.
        var narratedScenes = plan.Scenes
            .OrderBy(s => s.Order)
            .Where(s => !string.IsNullOrWhiteSpace(s.Narration))
            .ToList();
        var segments = narratedScenes.Select(s => s.Narration!).ToList();

        var outputPath = Path.Combine(_outputLocator.GetJobDirectory(jobId), NarrationFileName);
        TtsResult audio;
        try
        {
            audio = await _ttsProvider.SynthesizeAsync(new TtsRequest(segments, outputPath, Scenes: narratedScenes), cancellationToken);
        }
        catch (Exception ex) when (ex is TtsUnavailableException or TtsGenerationException)
        {
            job.MarkFailed(ex.Message, _timeProvider.GetUtcNow());
            await _repository.UpdateAsync(job, cancellationToken);
            throw;
        }

        // From here on the audio is the source of truth for timing: the LLM's guessed
        // durations are replaced, and the saved plan.json is what the renderer will play.
        // Fresh audio for exactly these words: the "narration outdated" mark (set by a text
        // edit, Day 23) no longer applies.
        var timedPlan = SceneTimingCalculator.Apply(plan, audio.Segments, audio.DurationSeconds, VideoFormat.Fps)
            with { NarrationOutdated = false };
        await _planStore.SaveAsync(jobId, timedPlan, cancellationToken);

        return new NarrationOutcome(audio, timedPlan);
    }
}
