namespace ShortsGenerator.Domain;

// Video rendering behind an interface (CLAUDE.md section 21): today a local Remotion
// process, later possibly a cloud renderer - callers only know "plan + audio in, MP4 out".
public interface IRenderer
{
    Task<RenderResult> RenderAsync(RenderRequest request, CancellationToken cancellationToken);

    Task<RendererAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken);
}

/// <param name="PlanPath">Absolute path to the (audio-timed) plan.json to render.</param>
/// <param name="NarrationPath">Absolute path to narration.wav, or null for a silent render.</param>
/// <param name="OutputPath">Absolute path of the .mp4 to create (or overwrite).</param>
public sealed record RenderRequest(Guid JobId, string PlanPath, string? NarrationPath, string OutputPath);

// Skipped (Day 27): the existing video was made from exactly these inputs and was reused.
public sealed record RenderResult(string OutputPath, long FileSizeBytes, TimeSpan Elapsed, bool Skipped = false);

public sealed record RendererAvailability(bool IsReady, string Engine, IReadOnlyList<string> Problems, string? SetupHint = null);

// The renderer isn't installed (Node missing, npm install never run, ...).
public sealed class RendererUnavailableException : Exception
{
    public RendererUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

// The renderer ran but couldn't produce a video for this job.
public sealed class RenderFailedException : Exception
{
    public RenderFailedException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
