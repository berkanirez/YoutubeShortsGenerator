using ShortsGenerator.Domain;

namespace ShortsGenerator.Application.Tests;

internal sealed class InMemoryVideoPlanStore : IVideoPlanStore
{
    private readonly Dictionary<Guid, VideoPlan> _plans = [];

    public Task<string> SaveAsync(Guid jobId, VideoPlan plan, CancellationToken cancellationToken)
    {
        _plans[jobId] = plan;
        return Task.FromResult($"output/{jobId}");
    }

    public Task<VideoPlan?> LoadAsync(Guid jobId, CancellationToken cancellationToken) =>
        Task.FromResult(_plans.GetValueOrDefault(jobId));
}
