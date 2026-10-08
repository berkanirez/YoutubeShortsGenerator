using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Infrastructure.Assets;

public static class AssetServiceCollectionExtensions
{
    public static IServiceCollection AddAssetRepository(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AssetManifestOptions>()
            .Bind(configuration.GetSection(AssetManifestOptions.SectionName))
            .PostConfigure<IHostEnvironment>((options, environment) =>
                options.ManifestPath = ContentRootPath.Resolve(environment, options.ManifestPath));

        services.AddSingleton<IAssetRepository, JsonAssetRepository>();

        return services;
    }
}
