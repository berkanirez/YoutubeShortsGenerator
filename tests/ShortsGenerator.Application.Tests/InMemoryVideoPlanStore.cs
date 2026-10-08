using ShortsGenerator.Domain;

namespace ShortsGenerator.Application.Tests;

// Keeps plans in memory; when given a locator it also drops a plan.json into the job's
// folder, because RenderService checks the files a real render would need actually exist.
internal sealed class InMemoryVideoPlanStore : IVideoPlanStore
{
    private readonly Dictionary<Guid, VideoPlan> _plans = [];
    private readonly IJobOutputLocator? _locator;

    public InMemoryVideoPlanStore(IJobOutputLocator? locator = null)
    {
        _locator = locator;
    }

    public Task<string> SaveAsync(Guid jobId, VideoPlan plan, CancellationToken cancellationToken)
    {
        _plans[jobId] = plan;
        if (_locator is null)
        {
            return Task.FromResult($"output/{jobId}");
        }

        var directory = _locator.GetJobDirectory(jobId);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "plan.json"), "{}");
        return Task.FromResult(directory);
    }

    public Task<VideoPlan?> LoadAsync(Guid jobId, CancellationToken cancellationToken) =>
        Task.FromResult(_plans.GetValueOrDefault(jobId));
}
