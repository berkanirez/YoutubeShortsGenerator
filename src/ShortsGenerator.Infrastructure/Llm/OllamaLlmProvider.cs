using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Infrastructure.Llm;

public sealed class OllamaLlmProvider : ILlmProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly OllamaOptions _options;
    private readonly ILogger<OllamaLlmProvider> _logger;

    public OllamaLlmProvider(HttpClient httpClient, IOptions<OllamaOptions> options, ILogger<OllamaLlmProvider>? logger = null)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger ?? NullLogger<OllamaLlmProvider>.Instance;
    }

    public Task<string> GenerateAsync(string prompt, CancellationToken cancellationToken) =>
        SendAsync(prompt, format: null, cancellationToken);

    // Ollama "structured outputs" (format = a JSON Schema): the schema is compiled into a
    // grammar that restricts which tokens the model may produce, so e.g. an array declared
    // with minItems = maxItems = 7 can only come out with 7 entries. Added on Day 15 after
    // the first real run spent 4 of 5 attempts on wrong scene counts and broken JSON.
    public Task<string> GenerateJsonAsync(string prompt, string jsonSchema, CancellationToken cancellationToken)
    {
        using var schema = JsonDocument.Parse(jsonSchema);
        return SendAsync(prompt, schema.RootElement.Clone(), cancellationToken);
    }

    private async Task<string> SendAsync(string prompt, JsonElement? format, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            throw new ArgumentException("Prompt is required.", nameof(prompt));
        }

        var request = new OllamaGenerateRequest(
            _options.Model, prompt, Stream: false, new OllamaGenerateOptions(_options.MaxOutputTokens), format,
            string.IsNullOrWhiteSpace(_options.KeepAlive) ? null : _options.KeepAlive);

        for (var attempt = 1; ; attempt++)
        {
            var isLastAttempt = attempt >= _options.MaxAttempts;
            HttpResponseMessage response;
            try
            {
                response = await _httpClient.PostAsJsonAsync("/api/generate", request, JsonOptions, cancellationToken);
            }
            catch (HttpRequestException ex) when (!isLastAttempt)
            {
                await WaitBeforeRetryAsync(attempt, ex.Message, cancellationToken);
                continue;
            }
            catch (HttpRequestException ex)
            {
                throw new LlmUnavailableException(
                    $"Could not reach Ollama at {_httpClient.BaseAddress}. Is it running? " +
                    "Start it from the Ollama app, or run 'ollama serve'.",
                    ex);
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                // Nobody cancelled: this is HttpClient.Timeout. Without this it would look like a
                // shutdown to the pipeline and surface as an "unexpected error". Not retried: a
                // second 2-minute wait would most likely end the same way.
                throw new LlmUnavailableException(
                    $"Ollama did not answer within {_httpClient.Timeout.TotalSeconds:0} seconds. The model may still be " +
                    "loading or the machine is busy; try again, or use a smaller model.",
                    ex);
            }

            if (response.StatusCode == HttpStatusCode.ServiceUnavailable && !isLastAttempt)
            {
                response.Dispose();
                await WaitBeforeRetryAsync(attempt, "HTTP 503", cancellationToken);
                continue;
            }

            return await ReadResponseAsync(response, cancellationToken);
        }
    }

    private async Task WaitBeforeRetryAsync(int attempt, string reason, CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "Ollama call failed ({Reason}); trying again in {Delay}s (attempt {Next} of {Max})",
            reason, _options.RetryDelaySeconds, attempt + 1, _options.MaxAttempts);
        await Task.Delay(TimeSpan.FromSeconds(_options.RetryDelaySeconds), cancellationToken);
    }

    private async Task<string> ReadResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using (response)
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new LlmUnavailableException(
                    $"Ollama model '{_options.Model}' is not available. Pull it first with " +
                    $"'ollama pull {_options.Model}'. Ollama said: {errorBody}");
            }

            if (!response.IsSuccessStatusCode)
            {
                // Ollama explains itself in {"error": "..."} (e.g. not enough memory for the model).
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new LlmUnavailableException(
                    $"Ollama returned HTTP {(int)response.StatusCode}: {ErrorText(errorBody)}");
            }

            try
            {
                var payload = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(JsonOptions, cancellationToken);
                if (payload is { TotalDuration: > 0 })
                {
                    // Ollama's own timing (nanoseconds) - where an LLM call's time actually goes
                    // (Day 27): loading the model, reading the prompt, or writing the answer.
                    _logger.LogInformation(
                        "Ollama {Model}: {Total:0.0}s total (load {Load:0.0}s, prompt {PromptTokens} tokens in {Prompt:0.0}s, " +
                        "answer {AnswerTokens} tokens in {Answer:0.0}s = {Rate:0.0} tokens/s)",
                        _options.Model, Seconds(payload.TotalDuration), Seconds(payload.LoadDuration),
                        payload.PromptEvalCount, Seconds(payload.PromptEvalDuration),
                        payload.EvalCount, Seconds(payload.EvalDuration),
                        payload.EvalDuration > 0 ? payload.EvalCount / Seconds(payload.EvalDuration) : 0);
                }

                return payload?.Response ?? string.Empty;
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException)
            {
                // NotSupportedException: a non-JSON content type (e.g. an HTML page from a proxy).
                throw new LlmUnavailableException(
                    $"Ollama at {_httpClient.BaseAddress} sent an unexpected response that isn't its JSON format.", ex);
            }
        }
    }

    private static double Seconds(long nanoseconds) => nanoseconds / 1e9;

    private static string ErrorText(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.ValueKind == JsonValueKind.Object &&
                json.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
            {
                return error.GetString()!;
            }
        }
        catch (JsonException)
        {
        }

        return body.Length > 300 ? body[..300] : body;
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

internal sealed record OllamaGenerateRequest(
    string Model,
    string Prompt,
    bool Stream,
    OllamaGenerateOptions Options,
    // Omitted from the request entirely when null (plain-text generation).
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    JsonElement? Format = null,
    [property: System.Text.Json.Serialization.JsonPropertyName("keep_alive")]
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    string? KeepAlive = null);

// Mirrors the subset of Ollama's "options" object we care about. num_predict caps how many
// tokens the model may generate; without it Ollama's own default can truncate a long JSON plan.
internal sealed record OllamaGenerateOptions(int NumPredict);

// Durations are nanoseconds, as Ollama reports them.
internal sealed record OllamaGenerateResponse(
    string Response,
    [property: System.Text.Json.Serialization.JsonPropertyName("total_duration")] long TotalDuration = 0,
    [property: System.Text.Json.Serialization.JsonPropertyName("load_duration")] long LoadDuration = 0,
    [property: System.Text.Json.Serialization.JsonPropertyName("prompt_eval_count")] int PromptEvalCount = 0,
    [property: System.Text.Json.Serialization.JsonPropertyName("prompt_eval_duration")] long PromptEvalDuration = 0,
    [property: System.Text.Json.Serialization.JsonPropertyName("eval_count")] int EvalCount = 0,
    [property: System.Text.Json.Serialization.JsonPropertyName("eval_duration")] long EvalDuration = 0);

internal sealed record OllamaTagsResponse(List<OllamaModelInfo> Models);

internal sealed record OllamaModelInfo(string Name);
