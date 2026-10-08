using Microsoft.EntityFrameworkCore;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Infrastructure;

public sealed class VideoJobRepository : IVideoJobRepository
{
    private readonly ShortsGeneratorDbContext _dbContext;

    public VideoJobRepository(ShortsGeneratorDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(VideoJob job, CancellationToken cancellationToken)
    {
        _dbContext.VideoJobs.Add(job);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(VideoJob job, CancellationToken cancellationToken)
    {
        _dbContext.VideoJobs.Update(job);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<VideoJob?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        _dbContext.VideoJobs.FirstOrDefaultAsync(job => job.Id == id, cancellationToken);

    public async Task<IReadOnlyList<VideoJob>> ListRecentAsync(int take, CancellationToken cancellationToken) =>
        await _dbContext.VideoJobs
            .OrderByDescending(job => job.CreatedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<VideoJob>> ListWithPendingWorkAsync(CancellationToken cancellationToken) =>
        await _dbContext.VideoJobs
            .Where(job => job.PendingWork != null)
            .OrderBy(job => job.UpdatedAt)
            .ToListAsync(cancellationToken);
}
