using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace ShortsGenerator.Api.Tests;

public class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"shortsgenerator-api-tests-{Guid.NewGuid()}.db");
    private readonly string _assetDirectory = Path.Combine(Path.GetTempPath(), $"shortsgenerator-api-tests-assets-{Guid.NewGuid()}");
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        // The test host's working directory is this test assembly's own bin output folder,
        // not src/ShortsGenerator.Api - the real app's "../../assets/manifests/assets.json"
        // relative path doesn't resolve there. Point the test at its own minimal, throwaway
        // manifest instead, same isolation reasoning as the SQLite file below.
        Directory.CreateDirectory(_assetDirectory);
        var manifestPath = Path.Combine(_assetDirectory, "assets.json");
        File.WriteAllText(Path.Combine(_assetDirectory, "placeholder.svg"), "<svg></svg>");
        File.WriteAllText(manifestPath, """
            [
              {
                "id": "placeholder",
                "filePath": "placeholder.svg",
                "category": "test",
                "tags": [],
                "compatibleSceneTypes": [],
                "orientation": "Square",
                "anchor": null
              }
            ]
            """);

        // Point this test run at its own throwaway SQLite file instead of the
        // developer's real local database, so tests never share or corrupt state.
        _factory = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, configBuilder) =>
                configBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:Default"] = $"Data Source={_dbPath}",
                    ["AssetManifest:ManifestPath"] = manifestPath,
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

        if (Directory.Exists(_assetDirectory))
        {
            Directory.Delete(_assetDirectory, recursive: true);
        }
    }
}
