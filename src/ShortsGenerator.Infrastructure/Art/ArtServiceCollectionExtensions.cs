using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Infrastructure.Art;

public static class ArtServiceCollectionExtensions
{
    public static IServiceCollection AddArtImport(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ArtImportOptions>()
            .Bind(configuration.GetSection(ArtImportOptions.SectionName))
            .PostConfigure<IHostEnvironment>((options, environment) =>
            {
                options.SubjectsPath = ContentRootPath.Resolve(environment, options.SubjectsPath);
                options.IncomingDirectory = ContentRootPath.Resolve(environment, options.IncomingDirectory);
                options.WorkDirectory = ContentRootPath.Resolve(environment, options.WorkDirectory);
                options.PythonPath = ContentRootPath.Resolve(environment, options.PythonPath);
                options.PrepareScriptPath = ContentRootPath.Resolve(environment, options.PrepareScriptPath);
                options.UpscalerPath = ContentRootPath.Resolve(environment, options.UpscalerPath);
            });

        services.AddSingleton<IArtSubjectCatalog, JsonArtSubjectCatalog>();
        services.AddSingleton<IArtImporter, ArtImporter>();
        return services;
    }
}
