namespace ShortsGenerator.Domain;

public interface IVideoPlanStore
{
    /// <summary>Persists the plan for this job and returns the directory it was saved under.</summary>
    Task<string> SaveAsync(Guid jobId, VideoPlan plan, CancellationToken cancellationToken);

    Task<VideoPlan?> LoadAsync(Guid jobId, CancellationToken cancellationToken);
}
