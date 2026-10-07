using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Infrastructure.Llm;

public static class LlmServiceCollectionExtensions
{
    public static IServiceCollection AddOllamaLlmProvider(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(OllamaOptions.SectionName);
        services.Configure<OllamaOptions>(section);

        var baseUrl = section[nameof(OllamaOptions.BaseUrl)] ?? new OllamaOptions().BaseUrl;

        services.AddHttpClient<ILlmProvider, OllamaLlmProvider>(client =>
        {
            client.BaseAddress = new Uri(baseUrl);
            // Local, possibly CPU-only generation can be slow; don't time out too eagerly.
            client.Timeout = TimeSpan.FromMinutes(2);
        });

        return services;
    }
}
