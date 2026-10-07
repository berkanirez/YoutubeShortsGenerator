using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Infrastructure.Llm;

public sealed class OllamaLlmProvider : ILlmProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly OllamaOptions _options;

    public OllamaLlmProvider(HttpClient httpClient, IOptions<OllamaOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new ArgumentException("Prompt is required.", nameof(prompt));
        }

        var request = new OllamaGenerateRequest(_options.Model, prompt, Stream: false);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync("/api/generate", request, JsonOptions, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new LlmUnavailableException(
                $"Could not reach Ollama at {_httpClient.BaseAddress}. Is it running? " +
                "Start it from the Ollama app, or run 'ollama serve'.",
                ex);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new LlmUnavailableException(
                $"Ollama model '{_options.Model}' is not available. Pull it first with " +
                $"'ollama pull {_options.Model}'. Ollama said: {errorBody}");
        }

        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(JsonOptions, cancellationToken);
        return payload?.Response ?? string.Empty;
    }

    public async Task<LlmAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken)
    {
        OllamaTagsResponse? tags;
        try
        {
            tags = await _httpClient.GetFromJsonAsync<OllamaTagsResponse>("/api/tags", JsonOptions, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            return new LlmAvailability(
                IsReachable: false,
                IsModelAvailable: false,
                Detail: $"Could not reach Ollama at {_httpClient.BaseAddress}: {ex.Message}");
        }

        var models = tags?.Models ?? [];
        var isModelAvailable = models.Any(m =>
            m.Name.Equals(_options.Model, StringComparison.OrdinalIgnoreCase) ||
            m.Name.StartsWith(_options.Model + ":", StringComparison.OrdinalIgnoreCase));

        return isModelAvailable
            ? new LlmAvailability(true, true, $"Ollama is reachable and '{_options.Model}' is pulled.")
            : new LlmAvailability(true, false, $"Ollama is reachable but '{_options.Model}' is not pulled. Run: ollama pull {_options.Model}");
    }
}

internal sealed record OllamaGenerateRequest(string Model, string Prompt, bool Stream);

internal sealed record OllamaGenerateResponse(string Response);

internal sealed record OllamaTagsResponse(List<OllamaModelInfo> Models);

internal sealed record OllamaModelInfo(string Name);
