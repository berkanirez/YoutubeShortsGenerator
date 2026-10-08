namespace ShortsGenerator.Domain;

// Writes a small text artifact (e.g. review.md) into a job's output folder.
public interface IJobArtifactWriter
{
    /// <param name="fileName">A plain file name like "review.md" - never a path.</param>
    Task<string> WriteTextAsync(Guid jobId, string fileName, string content, CancellationToken cancellationToken);
}
