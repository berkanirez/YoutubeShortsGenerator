using ShortsGenerator.Domain;

namespace ShortsGenerator.Application.Tests;

internal sealed class InMemoryAssetRepository : IAssetRepository
{
    private readonly IReadOnlyList<AssetMetadata> _assets;

    public InMemoryAssetRepository(params AssetMetadata[] assets)
    {
        _assets = assets;
    }

    public Task<IReadOnlyList<AssetMetadata>> GetAllAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_assets);

    public Task<AssetMetadata?> GetByIdAsync(string id, CancellationToken cancellationToken) =>
        Task.FromResult(_assets.FirstOrDefault(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase)));
}
