using ShortsGenerator.Domain;

namespace ShortsGenerator.Application;

public sealed class VideoJobService
{
    private readonly IVideoJobRepository _repository;
    private readonly TimeProvider _timeProvider;
    private readonly IArtSubjectCatalog? _subjects;
    private readonly IAssetRepository? _assets;

    public VideoJobService(IVideoJobRepository repository, TimeProvider timeProvider, IArtSubjectCatalog? subjects = null, IAssetRepository? assets = null)
    {
        _repository = repository;
        _timeProvider = timeProvider;
        _subjects = subjects;
        _assets = assets;
    }

    public async Task<VideoJob> CreateJobAsync(string topic, string? sourceScript, CancellationToken cancellationToken)
    {
        // An own script is used as written, so it's checked now: the user learns it's too short
        // in the response, not half a minute later from a failed background job.
        if (StoryboardScript.IsStructured(sourceScript) && _subjects is not null)
        {
            var catalog = _assets is null ? [] : (await _assets.GetAllAsync(cancellationToken)).Select(a => a.Id).ToList();
            var (_, _, problems) = StoryboardScript.Build(sourceScript!, topic, await _subjects.GetAllAsync(cancellationToken), catalog);
            if (problems.Count > 0)
            {
                throw new ArgumentException($"Your storyboard script can't be used: {string.Join(" ", problems)}", nameof(sourceScript));
            }
        }
        else if (!string.IsNullOrWhiteSpace(sourceScript))
        {
            var validation = ScriptValidator.Validate(sourceScript);
            if (!validation.IsValid)
            {
                throw new ArgumentException($"Your script can't be used: {string.Join(" ", validation.Reasons)}", nameof(sourceScript));
            }
        }

        var job = VideoJob.Create(topic, sourceScript, _timeProvider.GetUtcNow());
        await _repository.AddAsync(job, cancellationToken);
        return job;
    }

    public Task<VideoJob?> GetJobAsync(Guid id, CancellationToken cancellationToken) =>
        _repository.GetByIdAsync(id, cancellationToken);

    public Task<IReadOnlyList<VideoJob>> ListRecentJobsAsync(int take, CancellationToken cancellationToken) =>
        _repository.ListRecentAsync(take, cancellationToken);
}
