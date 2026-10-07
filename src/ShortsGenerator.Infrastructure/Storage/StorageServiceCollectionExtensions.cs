using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Infrastructure.Storage;

public static class StorageServiceCollectionExtensions
{
    public static IServiceCollection AddVideoPlanStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OutputStorageOptions>(configuration.GetSection(OutputStorageOptions.SectionName));
        services.AddSingleton<IVideoPlanStore, FileVideoPlanStore>();

        return services;
    }
}
