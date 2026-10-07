using ShortsGenerator.Domain;

namespace ShortsGenerator.Application;

public sealed class VideoJobService
{
    private readonly IVideoJobRepository _repository;
    private readonly TimeProvider _timeProvider;

    public VideoJobService(IVideoJobRepository repository, TimeProvider timeProvider)
    {
        _repository = repository;
        _timeProvider = timeProvider;
    }

    public async Task<VideoJob> CreateJobAsync(string topic, string? sourceScript, CancellationToken cancellationToken)
    {
        var job = VideoJob.Create(topic, sourceScript, _timeProvider.GetUtcNow());
        await _repository.AddAsync(job, cancellationToken);
        return job;
    }

    public Task<VideoJob?> GetJobAsync(Guid id, CancellationToken cancellationToken) =>
        _repository.GetByIdAsync(id, cancellationToken);

    public Task<IReadOnlyList<VideoJob>> ListRecentJobsAsync(int take, CancellationToken cancellationToken) =>
        _repository.ListRecentAsync(take, cancellationToken);
}
