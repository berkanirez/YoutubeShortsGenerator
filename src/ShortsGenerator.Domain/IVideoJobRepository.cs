namespace ShortsGenerator.Domain;

public interface IVideoJobRepository
{
    Task AddAsync(VideoJob job, CancellationToken cancellationToken);

    Task UpdateAsync(VideoJob job, CancellationToken cancellationToken);

    Task<VideoJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<VideoJob>> ListRecentAsync(int take, CancellationToken cancellationToken);
}
