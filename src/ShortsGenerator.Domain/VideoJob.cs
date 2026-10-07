namespace ShortsGenerator.Domain;

public sealed class VideoJob
{
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

    public void MarkNarrating(DateTimeOffset now) =>
        TransitionTo(VideoJobStatus.Narrating, now, VideoJobStatus.Planning);

    public void SetOutputDirectory(string outputDirectory, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("Output directory is required.", nameof(outputDirectory));
        }

        OutputDirectory = outputDirectory;
        UpdatedAt = now;
    }

    public void MarkRendering(DateTimeOffset now) =>
        TransitionTo(VideoJobStatus.Rendering, now, VideoJobStatus.Narrating);

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
            VideoJobStatus.Rendering);
        ErrorMessage = errorMessage;
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
