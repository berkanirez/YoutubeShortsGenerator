using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace ShortsGenerator.Api.Tests;

public class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"shortsgenerator-api-tests-{Guid.NewGuid()}.db");
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        // Point this test run at its own throwaway SQLite file instead of the
        // developer's real local database, so tests never share or corrupt state.
        _factory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configBuilder) =>
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = $"Data Source={_dbPath}",
                })));
    }

    [Fact]
    public async Task GetHealth_ReturnsOkWithStatusOk()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":\"ok\"", body);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }
}
