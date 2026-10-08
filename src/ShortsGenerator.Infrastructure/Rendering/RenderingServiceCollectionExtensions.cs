using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using ShortsGenerator.Domain;
using ShortsGenerator.Infrastructure.Processes;

namespace ShortsGenerator.Infrastructure.Rendering;

public static class RenderingServiceCollectionExtensions
{
    public static IServiceCollection AddRemotionRenderer(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RemotionOptions>()
            .Bind(configuration.GetSection(RemotionOptions.SectionName))
            .PostConfigure<IHostEnvironment>((options, environment) =>
                options.RendererDirectory = ContentRootPath.Resolve(environment, options.RendererDirectory));

        services.TryAddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IRenderer, RemotionRenderer>();

        return services;
    }
}
