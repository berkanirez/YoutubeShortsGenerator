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

    // Jobs are stored by reference, so "what was saved" is captured separately for tests that
    // care about *when* a field reached the database.
    private readonly Dictionary<Guid, VideoJobWorkKind?> _savedPendingWork = [];

    public Task UpdateAsync(VideoJob job, CancellationToken cancellationToken)
    {
        _jobs[job.Id] = job;
        _savedPendingWork[job.Id] = job.PendingWork;
        return Task.CompletedTask;
    }

    public VideoJobWorkKind? SavedPendingWork(Guid id) => _savedPendingWork.GetValueOrDefault(id);

    public Task<VideoJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_jobs.GetValueOrDefault(id));

    public Task<IReadOnlyList<VideoJob>> ListRecentAsync(int take, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<VideoJob>>(
            _jobs.Values.OrderByDescending(job => job.CreatedAt).Take(take).ToList());

    public Task<IReadOnlyList<VideoJob>> ListWithPendingWorkAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<VideoJob>>(
            _jobs.Values.Where(job => job.PendingWork is not null).OrderBy(job => job.UpdatedAt).ToList());
}
