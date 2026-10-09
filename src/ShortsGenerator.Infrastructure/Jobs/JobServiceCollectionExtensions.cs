using Microsoft.Extensions.DependencyInjection;
using ShortsGenerator.Application;

namespace ShortsGenerator.Infrastructure.Jobs;

public static class JobServiceCollectionExtensions
{
    public static IServiceCollection AddVideoJobQueue(this IServiceCollection services)
    {
        services.AddSingleton<IVideoJobQueue, ChannelVideoJobQueue>(_ => new ChannelVideoJobQueue());
        services.AddScoped<IVideoJobProcessor, VideoGenerationPipeline>();
        services.AddScoped<VideoJobScheduler>();
        services.AddScoped<JobRecoveryService>();
        services.AddScoped<OutputIndexService>();
        services.AddScoped<ReviewService>();
        services.AddHostedService<JobRecoveryHostedService>();

        // Registered as a singleton first so it can also be injected (e.g. to show which job
        // is running), then handed to the host as the same instance.
        services.AddSingleton<VideoJobWorker>();
        services.AddHostedService(provider => provider.GetRequiredService<VideoJobWorker>());

        return services;
    }
}
