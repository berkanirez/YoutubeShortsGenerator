using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ShortsGenerator.Domain;
using ShortsGenerator.Infrastructure.Llm;

namespace ShortsGenerator.Infrastructure.Tests;

public class OllamaLlmProviderTests
{
    [Fact]
    public async Task GenerateAsync_ReturnsResponseText_OnSuccess()
    {
        var provider = CreateProvider(_ => JsonResponse(HttpStatusCode.OK, new { response = "OK" }));

        var result = await provider.GenerateAsync("Say OK", CancellationToken.None);

        Assert.Equal("OK", result);
    }

    [Fact]
    public async Task GenerateJsonAsync_SendsTheSchemaAsOllamasFormat()
    {
        string? body = null;
        var provider = CreateProvider(request =>
        {
            body = request.Content!.ReadAsStringAsync().Result;
            return JsonResponse(HttpStatusCode.OK, new { response = "{}" });
        });

        await provider.GenerateJsonAsync("Plan it", """{"type":"object","properties":{"n":{"type":"integer"}}}""", CancellationToken.None);

        using var json = JsonDocument.Parse(body!);
        Assert.Equal("object", json.RootElement.GetProperty("format").GetProperty("type").GetString());
    }

    [Fact]
    public async Task GenerateAsync_PlainText_SendsNoFormatAtAll()
    {
        string? body = null;
        var provider = CreateProvider(request =>
        {
            body = request.Content!.ReadAsStringAsync().Result;
            return JsonResponse(HttpStatusCode.OK, new { response = "OK" });
        });

        await provider.GenerateAsync("Say OK", CancellationToken.None);

        using var json = JsonDocument.Parse(body!);
        Assert.False(json.RootElement.TryGetProperty("format", out _));
    }

    [Fact]
    public async Task GenerateAsync_Throws_WhenOllamaUnreachable()
    {
        var provider = CreateProvider(_ => throw new HttpRequestException("Connection refused"));

        await Assert.ThrowsAsync<LlmUnavailableException>(() => provider.GenerateAsync("Say OK", CancellationToken.None));
    }

    [Fact]
    public async Task GenerateAsync_Throws_WhenModelNotFound()
    {
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent("{\"error\":\"model 'llama3.2:3b' not found\"}", Encoding.UTF8, "application/json"),
        });

        var ex = await Assert.ThrowsAsync<LlmUnavailableException>(() => provider.GenerateAsync("Say OK", CancellationToken.None));
        Assert.Contains("pull", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateAsync_ServerError_ThrowsUnavailableWithOllamasOwnMessage()
    {
        // What Ollama really answers when the model doesn't fit in memory.
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("{\"error\":\"model requires more system memory (5.6 GiB) than is available (3.1 GiB)\"}", Encoding.UTF8, "application/json"),
        });

        var ex = await Assert.ThrowsAsync<LlmUnavailableException>(() => provider.GenerateAsync("Say OK", CancellationToken.None));
        Assert.Contains("500", ex.Message);
        Assert.Contains("more system memory", ex.Message);
    }

    [Fact]
    public async Task GenerateAsync_BodyIsNotOllamaJson_ThrowsUnavailableInsteadOfAJsonException()
    {
        var provider = CreateProvider(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>proxy error</html>", Encoding.UTF8, "text/html"),
        });

        var ex = await Assert.ThrowsAsync<LlmUnavailableException>(() => provider.GenerateAsync("Say OK", CancellationToken.None));
        Assert.Contains("unexpected", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateAsync_HttpClientTimeout_ThrowsUnavailableSayingItDidNotAnswer()
    {
        // HttpClient.Timeout surfaces as a TaskCanceledException although nobody cancelled.
        var provider = CreateProvider(_ => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 120 seconds elapsing.",
            new TimeoutException()));

        var ex = await Assert.ThrowsAsync<LlmUnavailableException>(() => provider.GenerateAsync("Say OK", CancellationToken.None));
        Assert.Contains("did not answer", ex.Message);
    }

    [Fact]
    public async Task GenerateAsync_CallerCancels_StillThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var provider = CreateProvider(_ => throw new TaskCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GenerateAsync("Say OK", cts.Token));
    }

    [Fact]
    public async Task CheckAvailabilityAsync_ReportsModelAvailable_WhenPresentInTags()
    {
        var provider = CreateProvider(_ => JsonResponse(HttpStatusCode.OK, new { models = new[] { new { name = "llama3.2:3b" } } }));

        var result = await provider.CheckAvailabilityAsync(CancellationToken.None);

        Assert.True(result.IsReachable);
        Assert.True(result.IsModelAvailable);
    }

    [Fact]
    public async Task CheckAvailabilityAsync_ReportsModelMissing_WhenAbsentFromTags()
    {
        var provider = CreateProvider(_ => JsonResponse(HttpStatusCode.OK, new { models = new[] { new { name = "phi3.5" } } }));

        var result = await provider.CheckAvailabilityAsync(CancellationToken.None);

        Assert.True(result.IsReachable);
        Assert.False(result.IsModelAvailable);
    }

    [Fact]
    public async Task CheckAvailabilityAsync_ReportsUnreachable_WhenConnectionFails()
    {
        var provider = CreateProvider(_ => throw new HttpRequestException("Connection refused"));

        var result = await provider.CheckAvailabilityAsync(CancellationToken.None);

        Assert.False(result.IsReachable);
        Assert.False(result.IsModelAvailable);
    }

    private static OllamaLlmProvider CreateProvider(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var httpClient = new HttpClient(new FakeHttpMessageHandler(responder))
        {
            BaseAddress = new Uri("http://localhost:11434"),
        };
        var options = Options.Create(new OllamaOptions { Model = "llama3.2:3b" });
        return new OllamaLlmProvider(httpClient, options);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, object value) => new(statusCode)
    {
        Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json"),
    };
}
