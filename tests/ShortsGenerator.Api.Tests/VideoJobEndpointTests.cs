using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShortsGenerator.Application;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Api.Tests;

public sealed class VideoJobEndpointTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"shortsgenerator-api-jobs-{Guid.NewGuid()}.db");
    private readonly string _assetDirectory = Path.Combine(Path.GetTempPath(), $"shortsgenerator-api-jobs-assets-{Guid.NewGuid()}");
    private readonly string _outputRoot = Path.Combine(Path.GetTempPath(), $"shortsgenerator-api-jobs-output-{Guid.NewGuid()}");
    private readonly RecordingProcessor _processor = new();
    private readonly WebApplicationFactory<Program> _factory;

    public VideoJobEndpointTests(WebApplicationFactory<Program> factory)
    {
        Directory.CreateDirectory(_assetDirectory);
        File.WriteAllText(Path.Combine(_assetDirectory, "placeholder.svg"), "<svg></svg>");
        var manifestPath = Path.Combine(_assetDirectory, "assets.json");
        File.WriteAllText(manifestPath, """
            [{ "id": "placeholder", "filePath": "placeholder.svg", "category": "test", "tags": [],
               "compatibleSceneTypes": [], "orientation": "Square", "anchor": null }]
            """);

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = $"Data Source={_dbPath}",
                ["AssetManifest:ManifestPath"] = manifestPath,
                ["OutputStorage:RootDirectory"] = _outputRoot,
            }));
            // The real worker keeps running; only what it does with a job is replaced, so
            // these tests never call Ollama, Kokoro or Remotion.
            builder.ConfigureTestServices(services => services.AddScoped<IVideoJobProcessor>(_ => _processor));
        });
    }

    [Fact]
    public async Task PostVideos_ReturnsAcceptedImmediately_AndTheWorkerPicksTheJobUp()
    {
        var client = _factory.CreateClient();

        var stopwatch = Stopwatch.StartNew();
        var response = await client.PostAsJsonAsync("/api/videos", new { topic = "Why Your Pull-Ups Aren't Increasing" });
        stopwatch.Stop();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"took {stopwatch.Elapsed}");
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal($"/api/videos/{id}", response.Headers.Location?.OriginalString);

        var processed = await _processor.WaitForAsync(id);
        Assert.Equal(VideoJobWorkKind.Generate, processed.Kind);
    }

    [Fact]
    public async Task PostVideos_AutoGenerateFalse_OnlyCreatesTheJob()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/videos", new { topic = "Rest days", autoGenerate = false });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await Task.Delay(300);
        Assert.False(_processor.Items.ContainsKey(id));
    }

    [Fact]
    public async Task PostRender_ForAJobWithoutNarration_Returns409WithoutQueueing()
    {
        var client = _factory.CreateClient();
        var created = await client.PostAsJsonAsync("/api/videos", new { topic = "Rest days", autoGenerate = false });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var response = await client.PostAsync($"/api/videos/{id}/render", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("Narrating", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PostGenerate_ResumesAManualJobThroughTheQueue()
    {
        var client = _factory.CreateClient();
        var created = await client.PostAsJsonAsync("/api/videos", new { topic = "Rest days", autoGenerate = false });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var response = await client.PostAsync($"/api/videos/{id}/generate", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(VideoJobWorkKind.Generate, (await _processor.WaitForAsync(id)).Kind);
    }

    [Fact]
    public async Task GetVideos_ListsRecentJobsNewestFirst()
    {
        var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/api/videos", new { topic = "First", autoGenerate = false });
        await client.PostAsJsonAsync("/api/videos", new { topic = "Second", autoGenerate = false });

        var jobs = await client.GetFromJsonAsync<JsonElement[]>("/api/videos");

        Assert.Equal(["Second", "First"], jobs!.Take(2).Select(j => j.GetProperty("topic").GetString()));
    }

    [Fact]
    public async Task PostVideos_EmptyTopic_Returns400()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/videos", new { topic = " " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Root_ServesTheDashboard()
    {
        var response = await _factory.CreateClient().GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Shorts Generator", await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await _factory.CreateClient().GetAsync("/app.js")).StatusCode);
    }

    [Fact]
    public async Task Video_IsServedWithRangeSupport_SoTheBrowserCanSeek()
    {
        var id = Guid.NewGuid();
        var directory = Path.Combine(_outputRoot, id.ToString());
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, "final.mp4"), Enumerable.Range(0, 1000).Select(i => (byte)i).ToArray());
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/videos/{id}/video");
        request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(100, 199);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal("video/mp4", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(100, (await response.Content.ReadAsByteArrayAsync()).Length);
    }

    [Fact]
    public async Task VideoAndReview_Return404ForAJobWithoutFiles()
    {
        var client = _factory.CreateClient();
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/videos/{id}/video")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/videos/{id}/review")).StatusCode);
    }

    [Fact]
    public async Task AssetSvg_IsServedAsAnImageWithAScriptForbiddingPolicy()
    {
        var response = await _factory.CreateClient().GetAsync("/api/assets/placeholder/svg");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/svg+xml", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("default-src 'none'", response.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal(HttpStatusCode.NotFound, (await _factory.CreateClient().GetAsync("/api/assets/nope/svg")).StatusCode);
    }

    [Fact]
    public async Task ChangeSceneAsset_ForAnUnknownJob_Is404()
    {
        var response = await _factory.CreateClient().PutAsJsonAsync(
            $"/api/videos/{Guid.NewGuid()}/scenes/s1/asset", new { assetId = "placeholder" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task EditScene_ForAnUnknownJob_Is404_AndApplyEditsToo()
    {
        var client = _factory.CreateClient();
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/videos/{id}/scenes/s1", new { onScreenText = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/videos/{id}/apply-edits", null)).StatusCode);
    }

    [Fact]
    public async Task EditScene_UnknownAnimationName_Is422WithTheAllowedValues()
    {
        var response = await _factory.CreateClient().PutAsJsonAsync(
            $"/api/videos/{Guid.NewGuid()}/scenes/s1", new { animation = "explode" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Contains("PunchZoom", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GetPlan_CorruptPlanFile_ReturnsAnActionableErrorWithoutAStackTrace()
    {
        var id = Guid.NewGuid();
        Directory.CreateDirectory(Path.Combine(_outputRoot, id.ToString()));
        await File.WriteAllTextAsync(Path.Combine(_outputRoot, id.ToString(), "plan.json"), "{ \"title\": ");

        var response = await _factory.CreateClient().GetAsync($"/api/videos/{id}/plan");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("plan.json", body);
        Assert.DoesNotContain("   at ", body);
    }

    [Fact]
    public async Task PostVideos_MalformedJsonBody_Is400NotAServerError()
    {
        var response = await _factory.CreateClient().PostAsync(
            "/api/videos", new StringContent("{ topic: ", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("   at ", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PostVideos_TooShortOwnScript_Is400WithTheReason_AndNoJobIsQueued()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync(
            "/api/videos", new { topic = "Rest days", sourceScript = "Too short to be a narration." });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("words", body);
        Assert.DoesNotContain("(Parameter", body);
        Assert.Empty(_processor.Items);
    }

    [Fact]
    public async Task PostVideos_BlankOwnScript_MeansWriteOneForMe()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync(
            "/api/videos", new { topic = "Rest days", sourceScript = "   ", autoGenerate = false });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task PostVideos_OverlongTopic_Is400()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync(
            "/api/videos", new { topic = new string('a', 201), autoGenerate = false });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetVideo_UnknownId_Is404_AndANonGuidIdDoesNotMatchAnyRoute()
    {
        var client = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/videos/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/videos/..%2F..%2Fwindows")).StatusCode);
    }

    [Fact]
    public async Task ApplyEdits_ForAJobWithoutNarration_Is409()
    {
        var client = _factory.CreateClient();
        var created = await client.PostAsJsonAsync("/api/videos", new { topic = "Rest days", autoGenerate = false });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var response = await client.PostAsync($"/api/videos/{id}/apply-edits", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.False(_processor.Items.ContainsKey(id));
    }

    [Fact]
    public async Task RenderAndApplyEdits_PlanWithAnAssetMissingFromTheCatalog_Are409NamingIt_AndNothingIsQueued()
    {
        var client = _factory.CreateClient();
        var created = await client.PostAsJsonAsync("/api/videos", new { topic = "Rest days", autoGenerate = false });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<ShortsGenerator.Domain.IVideoJobRepository>();
            var job = (await repository.GetByIdAsync(id, CancellationToken.None))!;
            job.MarkPlanning(DateTimeOffset.UtcNow);
            job.MarkNarrating(DateTimeOffset.UtcNow);
            await repository.UpdateAsync(job, CancellationToken.None);
            await scope.ServiceProvider.GetRequiredService<ShortsGenerator.Domain.IVideoPlanStore>().SaveAsync(id, new ShortsGenerator.Domain.VideoPlan(
                "T", "H", 30, "n",
                [new ShortsGenerator.Domain.Scene("s1", 1, ShortsGenerator.Domain.SceneType.Hook, 5, "n", "x", ["deleted-asset"],
                    ShortsGenerator.Domain.AnimationId.Pop, null, null, null)],
                null, null, null), CancellationToken.None);
        }

        foreach (var url in new[] { $"/api/videos/{id}/render", $"/api/videos/{id}/apply-edits" })
        {
            var response = await client.PostAsync(url, null);
            Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
            Assert.Contains("deleted-asset", await response.Content.ReadAsStringAsync());
        }

        Assert.False(_processor.Items.ContainsKey(id));
    }

    [Fact]
    public async Task EditSettings_ForAnUnknownJob_Is404()
    {
        var response = await _factory.CreateClient().PutAsJsonAsync(
            $"/api/videos/{Guid.NewGuid()}/settings", new { captionStyle = "boxed" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task OpenFolder_RequiresAProvablyLocalCaller()
    {
        // The in-memory test server has no remote IP at all; "can't prove it's local" must
        // mean "no" - the endpoint starts a process on the machine.
        var response = await _factory.CreateClient().PostAsync($"/api/videos/{Guid.NewGuid()}/open-folder", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    public void Dispose()
    {
        _factory.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_dbPath);
        Directory.Delete(_assetDirectory, recursive: true);
        if (Directory.Exists(_outputRoot))
        {
            Directory.Delete(_outputRoot, recursive: true);
        }
    }

    private sealed class RecordingProcessor : IVideoJobProcessor
    {
        public ConcurrentDictionary<Guid, VideoJobWorkItem> Items { get; } = new();

        public Task ProcessAsync(VideoJobWorkItem item, CancellationToken cancellationToken)
        {
            Items[item.JobId] = item;
            return Task.CompletedTask;
        }

        public async Task<VideoJobWorkItem> WaitForAsync(Guid jobId)
        {
            for (var i = 0; i < 500; i++)
            {
                if (Items.TryGetValue(jobId, out var item))
                {
                    return item;
                }

                await Task.Delay(10);
            }

            throw new TimeoutException($"Job {jobId} was never processed.");
        }
    }
}
