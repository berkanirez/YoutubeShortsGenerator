using ShortsGenerator.Domain;

namespace ShortsGenerator.Application;

public sealed class RenderService
{
    public const string PlanFileName = "plan.json";
    public const string FinalVideoFileName = "final.mp4";

    private readonly IRenderer _renderer;
    private readonly IVideoJobRepository _repository;
    private readonly IJobOutputLocator _outputLocator;
    private readonly TimeProvider _timeProvider;
    private readonly IVideoPlanStore? _planStore;
    private readonly IJobArtifactWriter? _artifactWriter;
    private readonly IAssetRepository? _assets;

    // planStore/artifactWriter/assets are optional so a render can run without the review
    // checklist or the catalog check (tests, tools); the API's DI container provides all three.
    public RenderService(
        IRenderer renderer,
        IVideoJobRepository repository,
        IJobOutputLocator outputLocator,
        TimeProvider timeProvider,
        IVideoPlanStore? planStore = null,
        IJobArtifactWriter? artifactWriter = null,
        IAssetRepository? assets = null)
    {
        _renderer = renderer;
        _repository = repository;
        _outputLocator = outputLocator;
        _timeProvider = timeProvider;
        _planStore = planStore;
        _artifactWriter = artifactWriter;
        _assets = assets;
    }

    // Things in the saved plan that would make the render fail, found in milliseconds instead
    // of after the renderer has started Chrome. Today: asset ids that aren't in the catalog
    // (any more) - e.g. after the manifest was edited by hand. Also called by the API before
    // queueing, so the user gets the reason right away and the job stays editable.
    public async Task<IReadOnlyList<string>> FindPlanProblemsAsync(Guid jobId, CancellationToken cancellationToken)
    {
        if (_planStore is null || _assets is null)
        {
            return [];
        }

        var plan = await _planStore.LoadAsync(jobId, cancellationToken);
        if (plan is null)
        {
            return [];
        }

        var known = (await _assets.GetAllAsync(cancellationToken)).Select(a => a.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return plan.Scenes
            .SelectMany(scene => scene.AssetIds.Where(id => !known.Contains(id)).Select(id =>
                $"Scene '{scene.Id}' uses asset '{id}', which is not in the asset catalog; pick another one with \"Change\"."))
            .ToList();
    }

    public async Task<VideoJob> RenderAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await _repository.GetByIdAsync(jobId, cancellationToken)
            ?? throw new KeyNotFoundException($"Video job '{jobId}' was not found.");

        if (job.Status is not (VideoJobStatus.Narrating or VideoJobStatus.Completed))
        {
            throw new InvalidOperationException(
                $"Video job '{jobId}' is '{job.Status}'; it can be rendered once narration exists (Narrating) or re-rendered when Completed.");
        }

        var jobDirectory = _outputLocator.GetJobDirectory(jobId);
        var planPath = Path.Combine(jobDirectory, PlanFileName);
        var narrationPath = Path.Combine(jobDirectory, NarrationService.NarrationFileName);
        if (!File.Exists(planPath) || !File.Exists(narrationPath))
        {
            throw new InvalidOperationException(
                $"Video job '{jobId}' is missing {(File.Exists(planPath) ? NarrationService.NarrationFileName : PlanFileName)}. " +
                "Generate the plan and narration before rendering.");
        }

        // A narration text edit (Day 23) means narration.wav and the scene timing describe the
        // old words: rendering now would put new captions over the old voice.
        if (_planStore is not null && (await _planStore.LoadAsync(jobId, cancellationToken))?.NarrationOutdated == true)
        {
            throw new InvalidOperationException(
                $"Video job '{jobId}' has edited narration text; regenerate the narration before rendering.");
        }

        var problems = await FindPlanProblemsAsync(jobId, cancellationToken);
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", problems));
        }

        // Same reasoning as narration: a missing renderer install is a machine problem,
        // checked before the job is touched so it doesn't burn the job as Failed.
        var availability = await _renderer.CheckAvailabilityAsync(cancellationToken);
        if (!availability.IsReady)
        {
            throw new RendererUnavailableException(
                $"Renderer ({availability.Engine}) is not ready: {string.Join("; ", availability.Problems)}." +
                (availability.SetupHint is null ? string.Empty : $" {availability.SetupHint}"));
        }

        job.MarkRendering(_timeProvider.GetUtcNow());
        await _repository.UpdateAsync(job, cancellationToken);

        RenderResult result;
        try
        {
            result = await _renderer.RenderAsync(
                new RenderRequest(jobId, planPath, narrationPath, Path.Combine(jobDirectory, FinalVideoFileName)),
                cancellationToken);
        }
        catch (Exception ex) when (ex is RendererUnavailableException or RenderFailedException or OperationCanceledException)
        {
            // Never leave the job in "Rendering": nothing would ever move it out again. On
            // cancellation the request's token is already cancelled, so the save uses None.
            var reason = ex is OperationCanceledException ? "Rendering was cancelled before it finished." : ex.Message;
            job.MarkFailed(reason, _timeProvider.GetUtcNow());
            await _repository.UpdateAsync(job, CancellationToken.None);
            throw;
        }

        job.MarkCompleted(result.OutputPath, _timeProvider.GetUtcNow());
        await _repository.UpdateAsync(job, cancellationToken);
        await WriteReviewChecklistAsync(job, cancellationToken);
        return job;
    }

    // Best effort: the video is done and valid even if its checklist can't be written; a
    // missing review.md is visible to the reviewer, a video marked Failed for it would be wrong.
    private async Task WriteReviewChecklistAsync(VideoJob job, CancellationToken cancellationToken)
    {
        if (_planStore is null || _artifactWriter is null)
        {
            return;
        }

        try
        {
            var plan = await _planStore.LoadAsync(job.Id, cancellationToken);
            if (plan is not null)
            {
                await _artifactWriter.WriteTextAsync(
                    job.Id, ReviewChecklist.FileName, ReviewChecklist.Build(job, plan, _timeProvider.GetUtcNow()), cancellationToken);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
