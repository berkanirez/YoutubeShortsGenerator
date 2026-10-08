using ShortsGenerator.Domain;

namespace ShortsGenerator.Application;

public enum ScheduleResult { Queued, AlreadyQueued, QueueFull }

// The one way work gets queued (Day 26): the intent is saved on the job first
// (VideoJob.PendingWork, SQLite), then put on the in-memory channel. If the API stops before
// the work finishes, JobRecoveryService finds the saved intent on the next start.
public sealed class VideoJobScheduler
{
    private readonly IVideoJobRepository _repository;
    private readonly IVideoJobQueue _queue;
    private readonly TimeProvider _timeProvider;

    public VideoJobScheduler(IVideoJobRepository repository, IVideoJobQueue queue, TimeProvider timeProvider)
    {
        _repository = repository;
        _queue = queue;
        _timeProvider = timeProvider;
    }

    public async Task<ScheduleResult> ScheduleAsync(VideoJob job, VideoJobWorkKind kind, CancellationToken cancellationToken)
    {
        // PendingWork covers "waiting" *and* "running": IsQueued alone forgets a job the moment
        // the worker picks it up, so a second click during its narration would queue it twice.
        if (job.PendingWork is not null || _queue.IsQueued(job.Id))
        {
            return ScheduleResult.AlreadyQueued;
        }

        // Saved before enqueueing: the worker may pick the item up immediately, and must not
        // find a job without its pending mark (it would then clear nothing and the mark would
        // be written after the work finished, resuming it again on the next start).
        job.MarkQueued(kind, _timeProvider.GetUtcNow());
        await _repository.UpdateAsync(job, cancellationToken);

        if (_queue.TryEnqueue(new VideoJobWorkItem(job.Id, kind)))
        {
            return ScheduleResult.Queued;
        }

        // Queue full: the intent mustn't stay saved, or the job would look busy forever.
        job.MarkWorkFinished(_timeProvider.GetUtcNow());
        await _repository.UpdateAsync(job, CancellationToken.None);
        return ScheduleResult.QueueFull;
    }
}
