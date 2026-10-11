using Microsoft.Extensions.Logging;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Application;

// Runs a job through script -> plan -> narration -> render, one stage at a time, deciding
// the next stage from the job's *persisted* state each time. That makes it resumable: a job
// created with autoGenerate=false and partly driven by hand, or one whose narration was
// regenerated manually, continues from where it actually is instead of starting over.
public sealed class VideoGenerationPipeline : IVideoJobProcessor
{
    private enum Stage { Script, Plan, Narration, Render }

    private readonly IVideoJobRepository _repository;
    private readonly IVideoPlanStore _planStore;
    private readonly ScriptGenerationService _scriptService;
    private readonly ScenePlanningService _planningService;
    private readonly NarrationService _narrationService;
    private readonly RenderService _renderService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<VideoGenerationPipeline> _logger;
    private readonly OutputIndexService? _outputIndex;
    private readonly ArtRequestService? _art;
    private readonly StoryboardService? _storyboard;

    public VideoGenerationPipeline(
        IVideoJobRepository repository,
        IVideoPlanStore planStore,
        ScriptGenerationService scriptService,
        ScenePlanningService planningService,
        NarrationService narrationService,
        RenderService renderService,
        TimeProvider timeProvider,
        ILogger<VideoGenerationPipeline> logger,
        OutputIndexService? outputIndex = null,
        ArtRequestService? art = null,
        StoryboardService? storyboard = null)
    {
        _storyboard = storyboard;
        _outputIndex = outputIndex;
        _art = art;
        _repository = repository;
        _planStore = planStore;
        _scriptService = scriptService;
        _planningService = planningService;
        _narrationService = narrationService;
        _renderService = renderService;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task ProcessAsync(VideoJobWorkItem item, CancellationToken cancellationToken)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object> { ["JobId"] = item.JobId });
        _logger.LogInformation("Job {JobId}: starting {Work}", item.JobId, item.Kind);
        var shuttingDown = false;
        try
        {
            if (item.Kind == VideoJobWorkKind.Render)
            {
                await RunStageAsync(Stage.Render, item.JobId, cancellationToken);
                return;
            }

            if (item.Kind == VideoJobWorkKind.Renarrate)
            {
                await RunStageAsync(Stage.Narration, item.JobId, cancellationToken, artGate: false);
                await RunStageAsync(Stage.Render, item.JobId, cancellationToken);
                return;
            }

            // Four stages at most; the bound guarantees a bug in stage selection can't loop forever.
            for (var i = 0; i < 4; i++)
            {
                var job = await _repository.GetByIdAsync(item.JobId, cancellationToken);
                if (job is null)
                {
                    _logger.LogWarning("Job {JobId} no longer exists; skipping", item.JobId);
                    return;
                }

                var next = await NextStageAsync(job, cancellationToken);
                if (next is null)
                {
                    _logger.LogInformation("Job {JobId} finished with status {Status}", job.Id, job.Status);
                    return;
                }

                await RunStageAsync(next.Value, job.Id, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // App shutdown: let the worker stop. The job keeps its PendingWork, so
            // JobRecoveryService resumes it on the next start.
            shuttingDown = true;
            _logger.LogWarning("Job {JobId}: interrupted by shutdown; it will resume on the next start", item.JobId);
            throw;
        }
        catch (Exception ex) when (IsExpectedStageFailure(ex))
        {
            // The stage services already marked the job Failed for job-specific errors. A
            // missing engine (LLM/TTS/renderer not installed) deliberately doesn't fail the
            // job in the manual endpoints - but nobody is watching a background run, so here
            // the job is failed with the same actionable message instead of sitting in
            // "Planning"/"Narrating" forever.
            _logger.LogWarning("Job {JobId} stopped: {Reason}", item.JobId, ex.Message);
            await FailIfStillRunningAsync(item.JobId, ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while processing job {JobId}", item.JobId);
            await FailIfStillRunningAsync(item.JobId, $"Unexpected error: {ex.Message}");
        }
        finally
        {
            if (!shuttingDown)
            {
                await MarkWorkFinishedAsync(item.JobId);
                if (_outputIndex is not null)
                {
                    await _outputIndex.UpdateBestEffortAsync(CancellationToken.None);
                }
            }
        }
    }

    // A plan carries the audio's timing once narration finished (Day 12), and loses validity
    // when its narration text is edited (Day 23). Narrating alone doesn't prove the audio is
    // done: the status is set when synthesis *starts*, so an interrupted narration must run again.
    public static bool IsNarrated(VideoPlan plan) =>
        !plan.NarrationOutdated && plan.Scenes.Any(s => s.SpokenDurationSeconds is not null);

    private async Task<Stage?> NextStageAsync(VideoJob job, CancellationToken cancellationToken) => job.Status switch
    {
        VideoJobStatus.Created => Stage.Script,
        VideoJobStatus.Planning when job.GeneratedScript is null => Stage.Script,
        VideoJobStatus.Planning => await _planStore.LoadAsync(job.Id, cancellationToken) is null
            ? Stage.Plan
            : Stage.Narration,
        VideoJobStatus.Narrating => await _planStore.LoadAsync(job.Id, cancellationToken) is { } plan && IsNarrated(plan)
            ? Stage.Render
            : Stage.Narration,
        _ => null, // Rendering (interrupted elsewhere), Completed, Failed, WaitingForArt
    };

    private async Task MarkWorkFinishedAsync(Guid jobId)
    {
        try
        {
            var job = await _repository.GetByIdAsync(jobId, CancellationToken.None);
            if (job is { PendingWork: not null })
            {
                job.MarkWorkFinished(_timeProvider.GetUtcNow());
                await _repository.UpdateAsync(job, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            // Worst case the job is resumed once more on the next start - not worth failing for.
            _logger.LogWarning(ex, "Job {JobId}: could not clear its pending-work mark", jobId);
        }
    }

    // artGate: a full generation checks the plan against the art catalog before narrating
    // (ArtRequestService); a manual re-narration of an existing video does not.
    private async Task RunStageAsync(Stage stage, Guid jobId, CancellationToken cancellationToken, bool artGate = true)
    {
        _logger.LogInformation("Job {JobId}: running stage {Stage}", jobId, stage);
        switch (stage)
        {
            case Stage.Script:
                // Storyboard formats write the script and the plan together; other topics, and
                // storyboards that don't come out valid, take the classic script path.
                if (_storyboard is not null && await _storyboard.TryGenerateAsync(jobId, cancellationToken))
                {
                    break;
                }

                await _scriptService.GenerateScriptAsync(jobId, cancellationToken);
                break;
            case Stage.Plan:
                await _planningService.GeneratePlanAsync(jobId, cancellationToken);
                break;
            case Stage.Narration:
                if (artGate && _art is not null && await _art.HoldForArtAsync(jobId, cancellationToken))
                {
                    break; // WaitingForArt: the next stage lookup finds nothing to do
                }

                await _narrationService.GenerateNarrationAsync(jobId, cancellationToken);
                break;
            case Stage.Render:
                await _renderService.RenderAsync(jobId, cancellationToken);
                break;
        }
    }

    private async Task FailIfStillRunningAsync(Guid jobId, string reason)
    {
        // Fresh read + CancellationToken.None: this runs after a failure, possibly one caused
        // by cancellation, and the failure itself must still be recorded.
        var job = await _repository.GetByIdAsync(jobId, CancellationToken.None);
        if (job is null || job.Status is VideoJobStatus.Completed or VideoJobStatus.Failed)
        {
            return;
        }

        job.MarkFailed(reason, _timeProvider.GetUtcNow());
        await _repository.UpdateAsync(job, CancellationToken.None);
    }

    private static bool IsExpectedStageFailure(Exception ex) => ex is
        LlmUnavailableException or ScriptGenerationException or VideoPlanGenerationException or
        TtsUnavailableException or TtsGenerationException or
        RendererUnavailableException or RenderFailedException or
        ArtImportException or InvalidOperationException;
}
