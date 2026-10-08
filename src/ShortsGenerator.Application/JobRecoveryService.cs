using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Application;

public sealed record RecoveryReport(IReadOnlyList<Guid> Resumed, IReadOnlyList<Guid> GaveUp);

// Day 26: getting jobs moving again after the API stopped (startup) or a job failed (Retry).
public sealed class JobRecoveryService
{
    private readonly IVideoJobRepository _repository;
    private readonly IVideoPlanStore _planStore;
    private readonly IJobOutputLocator _outputLocator;
    private readonly IVideoJobQueue _queue;
    private readonly VideoJobScheduler _scheduler;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<JobRecoveryService> _logger;

    public JobRecoveryService(
        IVideoJobRepository repository,
        IVideoPlanStore planStore,
        IJobOutputLocator outputLocator,
        IVideoJobQueue queue,
        VideoJobScheduler scheduler,
        TimeProvider timeProvider,
        ILogger<JobRecoveryService>? logger = null)
    {
        _repository = repository;
        _planStore = planStore;
        _outputLocator = outputLocator;
        _queue = queue;
        _scheduler = scheduler;
        _timeProvider = timeProvider;
        _logger = logger ?? NullLogger<JobRecoveryService>.Instance;
    }

    // Runs once at startup, before anything else can queue work: every job with pending
    // work was interrupted by the previous process ending. Jobs merely sitting in Planning
    // (created with autoGenerate=false, or from before Day 26) have nothing pending and are
    // left alone - resuming those would start generations nobody asked for.
    public async Task<RecoveryReport> RecoverInterruptedAsync(CancellationToken cancellationToken)
    {
        var resumed = new List<Guid>();
        var gaveUp = new List<Guid>();
        foreach (var job in await _repository.ListWithPendingWorkAsync(cancellationToken))
        {
            var interruptedWork = job.PendingWork!.Value;
            var interruptedStatus = job.Status;
            if (!job.RecoverAfterInterruption(_timeProvider.GetUtcNow()))
            {
                await _repository.UpdateAsync(job, cancellationToken);
                _logger.LogWarning("Job {JobId} was interrupted {Count} times in a row; not resuming it again", job.Id, job.InterruptionCount);
                gaveUp.Add(job.Id);
                continue;
            }

            await _repository.UpdateAsync(job, cancellationToken);
            // Generate resumes from the persisted state (the pipeline is resumable); a
            // Completed job's re-render/renarrate hasn't started, so it keeps its kind.
            var kind = job.Status is VideoJobStatus.Completed ? interruptedWork : VideoJobWorkKind.Generate;
            if (_queue.TryEnqueue(new VideoJobWorkItem(job.Id, kind)))
            {
                _logger.LogInformation(
                    "Resuming job {JobId} interrupted while {Status} ({Work}); attempt {Count}",
                    job.Id, interruptedStatus, interruptedWork, job.InterruptionCount);
                resumed.Add(job.Id);
            }
        }

        return new RecoveryReport(resumed, gaveUp);
    }

    // Puts a Failed job back at the last stage whose output is still valid on disk and queues
    // it. Nothing done so far is thrown away: a narration that exists is reused, etc.
    public async Task<VideoJob> RetryAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await _repository.GetByIdAsync(jobId, cancellationToken)
            ?? throw new KeyNotFoundException($"Video job '{jobId}' was not found.");
        if (job.Status is not VideoJobStatus.Failed)
        {
            throw new InvalidOperationException($"Video job '{jobId}' is '{job.Status}'; only failed jobs can be retried.");
        }

        var resumeAt = await ResumePointAsync(job, cancellationToken);
        _logger.LogInformation("Retrying failed job {JobId} from {Status} (was: {Error})", job.Id, resumeAt, job.ErrorMessage);
        job.RetryFrom(resumeAt, _timeProvider.GetUtcNow());
        await _repository.UpdateAsync(job, cancellationToken);

        if (await _scheduler.ScheduleAsync(job, VideoJobWorkKind.Generate, cancellationToken) is not ScheduleResult.Queued)
        {
            throw new InvalidOperationException("The job was reset but could not be queued (queue full); press Continue later.");
        }

        return job;
    }

    private async Task<VideoJobStatus> ResumePointAsync(VideoJob job, CancellationToken cancellationToken)
    {
        var plan = await _planStore.LoadAsync(job.Id, cancellationToken);
        var narration = Path.Combine(_outputLocator.GetJobDirectory(job.Id), NarrationService.NarrationFileName);
        if (plan is not null && VideoGenerationPipeline.IsNarrated(plan) && File.Exists(narration))
        {
            return VideoJobStatus.Narrating;     // render again
        }

        if (plan is not null || job.GeneratedScript is not null || job.SourceScript is not null)
        {
            return VideoJobStatus.Planning;      // the pipeline picks script/plan/narration from what exists
        }

        return VideoJobStatus.Created;
    }
}
