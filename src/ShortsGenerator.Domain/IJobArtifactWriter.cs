namespace ShortsGenerator.Domain;

// Writes a small text artifact (e.g. review.md) into a job's output folder.
public interface IJobArtifactWriter
{
    /// <param name="fileName">A plain file name like "review.md" - never a path.</param>
    Task<string> WriteTextAsync(Guid jobId, string fileName, string content, CancellationToken cancellationToken);

    /// <summary>Null when the file doesn't exist. Same file-name rule as writing.</summary>
    Task<string?> ReadTextAsync(Guid jobId, string fileName, CancellationToken cancellationToken);

    /// <summary>The overview of all finished videos, output/INDEX.md (Day 28).</summary>
    Task<string> WriteOutputIndexAsync(string content, CancellationToken cancellationToken);
}
