using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Infrastructure.Storage;

public static class StorageServiceCollectionExtensions
{
    public static IServiceCollection AddVideoPlanStorage(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<OutputStorageOptions>()
            .Bind(configuration.GetSection(OutputStorageOptions.SectionName))
            .PostConfigure<IHostEnvironment>((options, environment) =>
                options.RootDirectory = ContentRootPath.Resolve(environment, options.RootDirectory));

        services.AddSingleton<IJobOutputLocator, JobOutputLocator>();
        // Picked up by the logging system like the console provider (Day 26).
        services.AddSingleton<Microsoft.Extensions.Logging.ILoggerProvider, Logging.JobLogFileLoggerProvider>();
        services.AddSingleton<IVideoPlanStore, FileVideoPlanStore>();
        services.AddSingleton<IJobArtifactWriter, FileJobArtifactWriter>();

        return services;
    }
}
