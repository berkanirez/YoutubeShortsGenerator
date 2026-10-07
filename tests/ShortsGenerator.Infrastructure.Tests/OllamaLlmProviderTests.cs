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
