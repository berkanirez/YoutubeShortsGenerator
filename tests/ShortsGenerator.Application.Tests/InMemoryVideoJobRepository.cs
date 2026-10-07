using ShortsGenerator.Domain;

namespace ShortsGenerator.Application.Tests;

internal sealed class InMemoryVideoJobRepository : IVideoJobRepository
{
    private readonly Dictionary<Guid, VideoJob> _jobs = [];

    public Task AddAsync(VideoJob job, CancellationToken cancellationToken)
    {
        _jobs[job.Id] = job;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(VideoJob job, CancellationToken cancellationToken)
    {
        _jobs[job.Id] = job;
        return Task.CompletedTask;
    }

    public Task<VideoJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_jobs.GetValueOrDefault(id));

    public Task<IReadOnlyList<VideoJob>> ListRecentAsync(int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<VideoJob>>(
            _jobs.Values.OrderByDescending(job => job.CreatedAt).Take(take).ToList());
}
