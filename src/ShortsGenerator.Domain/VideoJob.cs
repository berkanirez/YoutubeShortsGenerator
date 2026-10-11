namespace ShortsGenerator.Domain;

public sealed class VideoJob
{
    // The topic goes straight into the LLM prompt; SQLite doesn't enforce HasMaxLength, so
    // the limit lives here. 200 is generous for a Shorts title (the dashboard's maxlength).
    public const int MaxTopicLength = 200;

    public Guid Id { get; private set; }
    public string Topic { get; private set; } = string.Empty;
    public string? SourceScript { get; private set; }
    public string? GeneratedScript { get; private set; }
    public VideoJobStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? OutputDirectory { get; private set; }
    public string? FinalVideoPath { get; private set; }

    // Day 26: what the job was queued for and hasn't finished yet. The queue itself lives in
    // memory; this is the durable half of it, so work interrupted by a stop/crash of the API
    // can be picked up again on the next start. Null = nothing pending.
    public VideoJobWorkKind? PendingWork { get; private set; }

    // How many times in a row pending work was found interrupted at startup. A job that
    // keeps taking the process down with it must not be resumed forever (poison message).
    public int InterruptionCount { get; private set; }

    public const int MaxInterruptions = 2;

    // Required by EF Core to materialize entities from the database.
    private VideoJob()
    {
    }

    public static VideoJob Create(string topic, string? sourceScript, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            throw new ArgumentException("Topic is required.", nameof(topic));
        }

        if (topic.Trim().Length > MaxTopicLength)
        {
            throw new ArgumentException($"Topic must be at most {MaxTopicLength} characters.", nameof(topic));
        }

        return new VideoJob
        {
            Id = Guid.NewGuid(),
            Topic = topic.Trim(),
            SourceScript = string.IsNullOrWhiteSpace(sourceScript) ? null : sourceScript.Trim(),
            Status = VideoJobStatus.Created,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    public void MarkPlanning(DateTimeOffset now) =>
        TransitionTo(VideoJobStatus.Planning, now, VideoJobStatus.Created);

    // The plan is done but needs art the catalog doesn't have; nothing runs until the owner
    // supplies it (or chooses to go on without), see ResumeAfterArt.
    public void MarkWaitingForArt(DateTimeOffset now) =>
        TransitionTo(VideoJobStatus.WaitingForArt, now, VideoJobStatus.Planning);

    // Back to "Planning with a finished plan", which the pipeline continues with narration.
    public void ResumeAfterArt(DateTimeOffset now) =>
        TransitionTo(VideoJobStatus.Planning, now, VideoJobStatus.WaitingForArt);

    // Attaching the script does NOT move the job to Narrating: per the pipeline in
    // CLAUDE.md (script -> scenes -> assets -> TTS -> render), both script generation
    // and scene planning happen while the job is still "Planning". Narrating is reserved
    // for when real TTS audio is produced (Day 11) via MarkNarrating below.
    public void AttachGeneratedScript(string generatedScript, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(generatedScript))
        {
            throw new ArgumentException("Generated script is required.", nameof(generatedScript));
        }

        if (Status != VideoJobStatus.Planning)
        {
            throw new InvalidOperationException(
                $"Cannot attach a generated script to video job {Id} while it is '{Status}'; it must be 'Planning'.");
        }

        GeneratedScript = generatedScript;
        UpdatedAt = now;
    }

    // From Completed too (Day 23): editing a scene's narration text means the voice-over has to
    // be regenerated, which puts a finished job back into Narrating (then Rendering, Completed).
    public void MarkNarrating(DateTimeOffset now) =>
        TransitionTo(VideoJobStatus.Narrating, now, VideoJobStatus.Planning, VideoJobStatus.Completed);

    public void SetOutputDirectory(string outputDirectory, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("Output directory is required.", nameof(outputDirectory));
        }

        OutputDirectory = outputDirectory;
        UpdatedAt = now;
    }

    // From Completed too: re-rendering a finished job (e.g. after a renderer/visual change)
    // shouldn't require regenerating its script and narration. The previous FinalVideoPath
    // stays set until the new render completes.
    public void MarkRendering(DateTimeOffset now) =>
        TransitionTo(VideoJobStatus.Rendering, now, VideoJobStatus.Narrating, VideoJobStatus.Completed);

    public void MarkCompleted(string finalVideoPath, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(finalVideoPath))
        {
            throw new ArgumentException("Final video path is required.", nameof(finalVideoPath));
        }

        TransitionTo(VideoJobStatus.Completed, now, VideoJobStatus.Rendering);
        FinalVideoPath = finalVideoPath;
    }

    public void MarkFailed(string errorMessage, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            throw new ArgumentException("Error message is required.", nameof(errorMessage));
        }

        TransitionTo(
            VideoJobStatus.Failed,
            now,
            VideoJobStatus.Created,
            VideoJobStatus.Planning,
            VideoJobStatus.Narrating,
            VideoJobStatus.Rendering,
            VideoJobStatus.WaitingForArt);
        ErrorMessage = errorMessage;
    }

    public void MarkQueued(VideoJobWorkKind kind, DateTimeOffset now)
    {
        PendingWork = kind;
        UpdatedAt = now;
    }

    // The work item ran to an end - success or a recorded failure. Not called when the API
    // shuts down mid-work: then PendingWork stays set and the work resumes on the next start.
    public void MarkWorkFinished(DateTimeOffset now)
    {
        PendingWork = null;
        InterruptionCount = 0;
        UpdatedAt = now;
    }

    /// <summary>
    /// Called at startup for a job whose work was interrupted. Returns false (and fails the
    /// job) once it has been interrupted more than <see cref="MaxInterruptions"/> times.
    /// </summary>
    public bool RecoverAfterInterruption(DateTimeOffset now)
    {
        if (PendingWork is null)
        {
            throw new InvalidOperationException($"Video job {Id} has no pending work to recover.");
        }

        InterruptionCount++;
        if (InterruptionCount > MaxInterruptions)
        {
            var reason = $"Stopped after being interrupted {InterruptionCount} times in a row while '{Status}' " +
                         "(the API stopped or crashed each time). See job.log, then use Retry.";
            PendingWork = null;
            if (Status is not (VideoJobStatus.Completed or VideoJobStatus.Failed))
            {
                MarkFailed(reason, now);
            }

            return false;
        }

        // A render that was cut off produced nothing (it writes final.rendering.mp4 and only
        // renames on success, Day 13); its inputs are intact, so it starts over from Narrating.
        if (Status is VideoJobStatus.Rendering)
        {
            Status = VideoJobStatus.Narrating;
        }

        UpdatedAt = now;
        return true;
    }

    /// <summary>
    /// Puts a failed job back at the last stage whose results are still valid, chosen by the
    /// caller from what exists on disk. Its error is cleared.
    /// </summary>
    public void RetryFrom(VideoJobStatus resumeAt, DateTimeOffset now)
    {
        if (resumeAt is not (VideoJobStatus.Created or VideoJobStatus.Planning or VideoJobStatus.Narrating))
        {
            throw new ArgumentOutOfRangeException(nameof(resumeAt), resumeAt, "A retry resumes at Created, Planning or Narrating.");
        }

        TransitionTo(resumeAt, now, VideoJobStatus.Failed);
        ErrorMessage = null;
        InterruptionCount = 0;
    }

    private void TransitionTo(VideoJobStatus target, DateTimeOffset now, params VideoJobStatus[] allowedFrom)
    {
        if (Array.IndexOf(allowedFrom, Status) < 0)
        {
            throw new InvalidOperationException(
                $"Cannot move video job {Id} from status '{Status}' to '{target}'.");
        }

        Status = target;
        UpdatedAt = now;
    }
}
