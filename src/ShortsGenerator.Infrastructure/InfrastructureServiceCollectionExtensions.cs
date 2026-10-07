using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Missing 'ConnectionStrings:Default' configuration value.");

        services.AddDbContext<ShortsGeneratorDbContext>(options => options.UseSqlite(connectionString));
        services.AddScoped<IVideoJobRepository, VideoJobRepository>();
        services.AddSingleton(TimeProvider.System);

        return services;
    }
}
