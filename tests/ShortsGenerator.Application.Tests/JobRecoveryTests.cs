using ShortsGenerator.Domain;

namespace ShortsGenerator.Application.Tests;

// Day 26: the persisted half of the queue (VideoJob.PendingWork), startup recovery and Retry.
public sealed class JobRecoveryTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryVideoJobRepository _repository = new();
    private readonly TempJobOutputLocator _locator = new();
    private readonly InMemoryVideoPlanStore _planStore;
    private readonly RecordingQueue _queue = new();

    public JobRecoveryTests()
    {
        _planStore = new InMemoryVideoPlanStore(_locator);
    }

    public void Dispose() => _locator.Dispose();

    // --- scheduler ---

    [Fact]
    public async Task Schedule_SavesTheIntentBeforeTheWorkerCanSeeTheItem()
    {
        var job = await AddAsync(VideoJobStatus.Created);
        VideoJobWorkKind? pendingWhenEnqueued = null;
        _queue.OnEnqueue = item => pendingWhenEnqueued = _repository.SavedPendingWork(item.JobId);

        var result = await Scheduler().ScheduleAsync(job, VideoJobWorkKind.Generate, CancellationToken.None);

        Assert.Equal(ScheduleResult.Queued, result);
        Assert.Equal(VideoJobWorkKind.Generate, pendingWhenEnqueued);
    }

    [Fact]
    public async Task Schedule_AJobThatIsStillRunning_IsAlreadyQueued_EvenThoughTheChannelNoLongerHasIt()
    {
        var job = await AddAsync(VideoJobStatus.Narrating);
        job.MarkQueued(VideoJobWorkKind.Renarrate, Now);   // picked up by the worker: not in the channel any more

        var result = await Scheduler().ScheduleAsync(job, VideoJobWorkKind.Render, CancellationToken.None);

        Assert.Equal(ScheduleResult.AlreadyQueued, result);
        Assert.Empty(_queue.Items);
    }

    [Fact]
    public async Task Schedule_QueueFull_LeavesNoIntentBehind()
    {
        var job = await AddAsync(VideoJobStatus.Created);
        _queue.IsFull = true;

        var result = await Scheduler().ScheduleAsync(job, VideoJobWorkKind.Generate, CancellationToken.None);

        Assert.Equal(ScheduleResult.QueueFull, result);
        Assert.Null(job.PendingWork);
    }

    // --- startup recovery ---

    [Fact]
    public async Task Recover_ResumesOnlyJobsWithPendingWork_FromWhereTheyAre()
    {
        var interrupted = await AddAsync(VideoJobStatus.Planning, pending: VideoJobWorkKind.Generate);
        var paused = await AddAsync(VideoJobStatus.Planning);   // autoGenerate=false: nobody asked for more

        var report = await Recovery().RecoverInterruptedAsync(CancellationToken.None);

        Assert.Equal([interrupted.Id], report.Resumed);
        Assert.Equal([new VideoJobWorkItem(interrupted.Id, VideoJobWorkKind.Generate)], _queue.Items);
        Assert.Null(paused.PendingWork);
        Assert.Equal(1, interrupted.InterruptionCount);
    }

    [Fact]
    public async Task Recover_AnInterruptedRender_StartsAgainFromNarrating()
    {
        var job = await AddAsync(VideoJobStatus.Rendering, pending: VideoJobWorkKind.Render);

        await Recovery().RecoverInterruptedAsync(CancellationToken.None);

        Assert.Equal(VideoJobStatus.Narrating, job.Status);
        Assert.Equal(VideoJobWorkKind.Generate, Assert.Single(_queue.Items).Kind);
    }

    [Fact]
    public async Task Recover_ACompletedJobsReRenderThatHadNotStarted_KeepsItsKind()
    {
        var job = await AddAsync(VideoJobStatus.Completed, pending: VideoJobWorkKind.Renarrate);

        await Recovery().RecoverInterruptedAsync(CancellationToken.None);

        Assert.Equal(new VideoJobWorkItem(job.Id, VideoJobWorkKind.Renarrate), Assert.Single(_queue.Items));
    }

    [Fact]
    public async Task Recover_AJobInterruptedAThirdTimeInARow_IsFailedInsteadOfResumedForever()
    {
        var job = await AddAsync(VideoJobStatus.Narrating, pending: VideoJobWorkKind.Generate);
        job.RecoverAfterInterruption(Now);
        job.RecoverAfterInterruption(Now);

        var report = await Recovery().RecoverInterruptedAsync(CancellationToken.None);

        Assert.Equal([job.Id], report.GaveUp);
        Assert.Empty(_queue.Items);
        Assert.Equal(VideoJobStatus.Failed, job.Status);
        Assert.Contains("interrupted 3 times", job.ErrorMessage);
        Assert.Null(job.PendingWork);
    }

    // --- retry ---

    [Fact]
    public async Task Retry_WithANarratedPlanAndItsAudio_RendersAgainWithoutRedoingAnything()
    {
        var job = await AddAsync(VideoJobStatus.Rendering);
        await _planStore.SaveAsync(job.Id, Plan(narrated: true), CancellationToken.None);
        await File.WriteAllBytesAsync(Path.Combine(_locator.GetJobDirectory(job.Id), "narration.wav"), [1]);
        job.MarkFailed("chrome crashed", Now);

        await Recovery().RetryAsync(job.Id, CancellationToken.None);

        Assert.Equal(VideoJobStatus.Narrating, job.Status);
        Assert.Null(job.ErrorMessage);
        Assert.Equal(VideoJobWorkKind.Generate, job.PendingWork);
        Assert.Single(_queue.Items);
    }

    [Fact]
    public async Task Retry_PlanWhoseNarrationWasNeverFinished_GoesBackToPlanning_SoNarrationRunsAgain()
    {
        var job = await AddAsync(VideoJobStatus.Narrating);
        await _planStore.SaveAsync(job.Id, Plan(narrated: false), CancellationToken.None);
        job.MarkFailed("kokoro crashed", Now);

        await Recovery().RetryAsync(job.Id, CancellationToken.None);

        Assert.Equal(VideoJobStatus.Planning, job.Status);
    }

    [Fact]
    public async Task Retry_NothingProducedYet_StartsFromCreated()
    {
        var job = await AddAsync(VideoJobStatus.Created);
        job.MarkFailed("ollama down", Now);

        await Recovery().RetryAsync(job.Id, CancellationToken.None);

        Assert.Equal(VideoJobStatus.Created, job.Status);
    }

    [Fact]
    public async Task Retry_AJobThatDidNotFail_IsRefused()
    {
        var job = await AddAsync(VideoJobStatus.Completed);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => Recovery().RetryAsync(job.Id, CancellationToken.None));
        Assert.Contains("only failed jobs", ex.Message);
    }

    private VideoJobScheduler Scheduler() => new(_repository, _queue, TimeProvider.System);

    private JobRecoveryService Recovery() =>
        new(_repository, _planStore, _locator, _queue, Scheduler(), TimeProvider.System);

    private async Task<VideoJob> AddAsync(VideoJobStatus status, VideoJobWorkKind? pending = null)
    {
        var job = VideoJob.Create("Pull-ups", null, Now);
        if (status is not VideoJobStatus.Created)
        {
            job.MarkPlanning(Now);
        }

        if (status is VideoJobStatus.Narrating or VideoJobStatus.Rendering or VideoJobStatus.Completed)
        {
            job.AttachGeneratedScript("script", Now);
            job.MarkNarrating(Now);
        }

        if (status is VideoJobStatus.Rendering or VideoJobStatus.Completed)
        {
            job.MarkRendering(Now);
        }

        if (status is VideoJobStatus.Completed)
        {
            job.MarkCompleted("final.mp4", Now);
        }

        if (pending is { } kind)
        {
            job.MarkQueued(kind, Now);
        }

        await _repository.AddAsync(job, CancellationToken.None);
        return job;
    }

    private static VideoPlan Plan(bool narrated) => new(
        "T", "H", 10, "n",
        [new Scene("s1", 1, SceneType.Hook, 3, "n", "x", ["mascot-idle"], AnimationId.Pop, null, null, null,
            SpokenDurationSeconds: narrated ? 2.8 : null)],
        null, null, null);
}
