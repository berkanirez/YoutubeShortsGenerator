using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using ShortsGenerator.Domain;
using ShortsGenerator.Infrastructure.Processes;

namespace ShortsGenerator.Infrastructure.Tts;

public static class TtsServiceCollectionExtensions
{
    public static IServiceCollection AddKokoroTtsProvider(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<KokoroTtsOptions>()
            .Bind(configuration.GetSection(KokoroTtsOptions.SectionName))
            .PostConfigure<IHostEnvironment>((options, environment) =>
            {
                options.PythonPath = ContentRootPath.Resolve(environment, options.PythonPath);
                options.ScriptPath = ContentRootPath.Resolve(environment, options.ScriptPath);
                options.ModelPath = ContentRootPath.Resolve(environment, options.ModelPath);
                options.VoicesPath = ContentRootPath.Resolve(environment, options.VoicesPath);
            });

        // TryAdd: the renderer adapter (Day 13) registers the same runner.
        services.TryAddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<ITtsProvider, KokoroTtsProvider>();

        return services;
    }
}
