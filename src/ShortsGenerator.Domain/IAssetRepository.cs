namespace ShortsGenerator.Domain;

public interface IAssetRepository
{
    Task<IReadOnlyList<AssetMetadata>> GetAllAsync(CancellationToken cancellationToken);

    Task<AssetMetadata?> GetByIdAsync(string id, CancellationToken cancellationToken);
}
