using Microsoft.EntityFrameworkCore;
using ShortsGenerator.Api.Contracts;
using ShortsGenerator.Api.Endpoints;
using ShortsGenerator.Application;
using ShortsGenerator.Domain;
using ShortsGenerator.Infrastructure;
using ShortsGenerator.Infrastructure.Assets;
using ShortsGenerator.Infrastructure.Jobs;
using ShortsGenerator.Infrastructure.Llm;
using ShortsGenerator.Infrastructure.Rendering;
using ShortsGenerator.Infrastructure.Storage;
using ShortsGenerator.Infrastructure.Tts;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddOllamaLlmProvider(builder.Configuration);
builder.Services.AddVideoPlanStorage(builder.Configuration);
builder.Services.AddAssetRepository(builder.Configuration);
builder.Services.AddKokoroTtsProvider(builder.Configuration);
builder.Services.AddRemotionRenderer(builder.Configuration);
builder.Services.AddScoped<VideoJobService>();
builder.Services.AddScoped<ScriptGenerationService>();
builder.Services.AddScoped<ScenePlanningService>();
builder.Services.AddScoped<NarrationService>();
builder.Services.AddScoped<RenderService>();
builder.Services.AddScoped<PlanEditService>();
builder.Services.AddVideoJobQueue();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Single local user, single process: applying migrations on startup is safe
// and keeps local setup to "just run it".
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ShortsGeneratorDbContext>();
    db.Database.Migrate();

    // JsonAssetRepository loads/validates the manifest in its constructor; resolving it
    // here fails the app at startup with a clear error if the manifest is missing/broken,
    // instead of surfacing a confusing error on the first real scene-planning request.
    scope.ServiceProvider.GetRequiredService<IAssetRepository>();
}

app.UseJsonErrors();
app.UseHttpsRedirection();
app.UseDashboard();
app.MapDashboardEndpoints();

app.MapGet("/api/health", () => Results.Ok(new HealthResponse("ok", DateTimeOffset.UtcNow)))
    .WithName("GetHealth");

// Creates the job and, by default, queues the whole pipeline for the background worker:
// the request returns in milliseconds (202 Accepted) and the client polls GET /api/videos/{id}.
// autoGenerate=false only creates the job, for driving the stages by hand.
app.MapPost("/api/videos", async (
    CreateVideoJobRequest request, VideoJobService service, VideoJobScheduler scheduler, IVideoJobQueue queue, CancellationToken ct) =>
{
    VideoJob job;
    try
    {
        job = await service.CreateJobAsync(request.Topic, request.SourceScript, ct);
    }
    catch (ArgumentException ex)
    {
        // ArgumentException.Message ends in " (Parameter 'x')" - noise for the person reading it.
        var message = ex.ParamName is null ? ex.Message : ex.Message.Replace($" (Parameter '{ex.ParamName}')", string.Empty);
        return Results.BadRequest(new { error = message });
    }

    if (!request.AutoGenerate)
    {
        return Results.Created($"/api/videos/{job.Id}", VideoJobResponse.FromDomain(job, queue));
    }

    return await EnqueueOrConflictAsync(scheduler, queue, job, VideoJobWorkKind.Generate, ct);
})
    .WithName("CreateVideoJob");

app.MapGet("/api/videos", async (
    VideoJobService service, IVideoJobQueue queue, VideoJobWorker worker, IJobOutputLocator locator, ReviewService reviews,
    CancellationToken ct, int take = 20) =>
{
    var jobs = await service.ListRecentJobsAsync(Math.Clamp(take, 1, 100), ct);
    var responses = new List<VideoJobResponse>();
    foreach (var job in jobs)
    {
        var reviewed = job.Status is VideoJobStatus.Completed && (await reviews.GetAsync(job.Id, ct)).IsReviewed;
        responses.Add(VideoJobResponse.FromDomain(
            job, queue, worker.CurrentJobId, DashboardEndpoints.IsVideoOutdated(locator, job.Id), reviewed));
    }

    return Results.Ok(responses);
})
    .WithName("ListVideoJobs");

app.MapGet("/api/videos/{id:guid}", async (
    Guid id, VideoJobService service, IVideoJobQueue queue, VideoJobWorker worker, IJobOutputLocator locator,
    ReviewService reviews, CancellationToken ct) =>
{
    var job = await service.GetJobAsync(id, ct);
    if (job is null)
    {
        return Results.NotFound();
    }

    var reviewed = job.Status is VideoJobStatus.Completed && (await reviews.GetAsync(id, ct)).IsReviewed;
    return Results.Ok(VideoJobResponse.FromDomain(job, queue, worker.CurrentJobId, DashboardEndpoints.IsVideoOutdated(locator, id), reviewed));
})
    .WithName("GetVideoJob");

// Continues a job from wherever it is (e.g. one created with autoGenerate=false).
app.MapPost("/api/videos/{id:guid}/generate", async (
    Guid id, VideoJobService service, VideoJobScheduler scheduler, IVideoJobQueue queue, CancellationToken ct) =>
{
    var job = await service.GetJobAsync(id, ct);
    if (job is null)
    {
        return Results.NotFound();
    }

    if (job.Status is VideoJobStatus.Completed or VideoJobStatus.Failed or VideoJobStatus.Rendering)
    {
        return Results.Conflict(new { error = $"Video job '{id}' is '{job.Status}'; there is nothing left to generate." });
    }

    return await EnqueueOrConflictAsync(scheduler, queue, job, VideoJobWorkKind.Generate, ct);
})
    .WithName("GenerateVideoJob");

// Day 26: a Failed job goes back to the last stage whose output is still valid on disk
// (script, plan, narrated plan + audio) and is queued from there.
app.MapPost("/api/videos/{id:guid}/retry", async (
    Guid id, JobRecoveryService recovery, IVideoJobQueue queue, CancellationToken ct) =>
{
    try
    {
        var job = await recovery.RetryAsync(id, ct);
        return Results.Accepted($"/api/videos/{id}", VideoJobResponse.FromDomain(job, queue));
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound();
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
})
    .WithName("RetryVideoJob");

// Day 28: the human review checklist, per video. Recording only - nothing is ever uploaded.
app.MapGet("/api/videos/{id:guid}/review-state", async (Guid id, VideoJobService service, ReviewService reviews, CancellationToken ct) =>
    await service.GetJobAsync(id, ct) is null ? Results.NotFound() : Results.Ok(await reviews.GetAsync(id, ct)))
    .WithName("GetReviewState");

app.MapPut("/api/videos/{id:guid}/review-state", async (Guid id, ReviewStateRequest request, ReviewService reviews, CancellationToken ct) =>
{
    try
    {
        return Results.Ok(await reviews.SaveAsync(id, request.Checked ?? [], ct));
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound();
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
    catch (ArgumentException ex)
    {
        return Results.UnprocessableEntity(new { error = ex.Message });
    }
})
    .WithName("SaveReviewState");

app.MapPost("/api/videos/{id:guid}/script", async (Guid id, ScriptGenerationService service, CancellationToken ct) =>
{
    try
    {
        var job = await service.GenerateScriptAsync(id, ct);
        return Results.Ok(VideoJobResponse.FromDomain(job));
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound();
    }
    catch (LlmUnavailableException ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (ScriptGenerationException ex)
    {
        return Results.UnprocessableEntity(new { error = ex.Message });
    }
})
    .WithName("GenerateVideoJobScript");

app.MapPost("/api/videos/{id:guid}/plan", async (Guid id, ScenePlanningService service, CancellationToken ct) =>
{
    try
    {
        var plan = await service.GeneratePlanAsync(id, ct);
        return Results.Ok(plan);
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound();
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
    catch (LlmUnavailableException ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (VideoPlanGenerationException ex)
    {
        return Results.UnprocessableEntity(new { error = ex.Message });
    }
})
    .WithName("GenerateVideoJobPlan");

app.MapGet("/api/videos/{id:guid}/plan", async (Guid id, IVideoPlanStore planStore, CancellationToken ct) =>
{
    var plan = await planStore.LoadAsync(id, ct);
    return plan is null ? Results.NotFound() : Results.Ok(plan);
})
    .WithName("GetVideoJobPlan");

app.MapPost("/api/videos/{id:guid}/narration", async (Guid id, NarrationService service, CancellationToken ct) =>
{
    try
    {
        var result = await service.GenerateNarrationAsync(id, ct);
        return Results.Ok(result);
    }
    catch (KeyNotFoundException)
    {
        return Results.NotFound();
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }
    catch (TtsUnavailableException ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (TtsGenerationException ex)
    {
        return Results.UnprocessableEntity(new { error = ex.Message });
    }
})
    .WithName("GenerateVideoJobNarration");

// Render / re-render goes through the queue as well: renders must run one at a time, and a
// 15s-2min render doesn't belong inside an HTTP request. Obvious ordering mistakes are still
// rejected synchronously so the caller gets a 409 instead of a job that fails later.
app.MapPost("/api/videos/{id:guid}/render", async (
    Guid id, VideoJobService service, RenderService renders, VideoJobScheduler scheduler, IVideoJobQueue queue, CancellationToken ct) =>
{
    var job = await service.GetJobAsync(id, ct);
    if (job is null)
    {
        return Results.NotFound();
    }

    if (job.Status is not (VideoJobStatus.Narrating or VideoJobStatus.Completed))
    {
        return Results.Conflict(new
        {
            error = $"Video job '{id}' is '{job.Status}'; it can be rendered once narration exists (Narrating) or re-rendered when Completed.",
        });
    }

    var problems = await renders.FindPlanProblemsAsync(id, ct);
    if (problems.Count > 0)
    {
        return Results.Conflict(new { error = string.Join(" ", problems) });
    }

    return await EnqueueOrConflictAsync(scheduler, queue, job, VideoJobWorkKind.Render, ct);
})
    .WithName("RenderVideoJob");

app.MapGet("/api/renderer/health", async (IRenderer renderer, CancellationToken ct) =>
{
    var availability = await renderer.CheckAvailabilityAsync(ct);
    return availability.IsReady
        ? Results.Ok(availability)
        : Results.Json(availability, statusCode: StatusCodes.Status503ServiceUnavailable);
})
    .WithName("GetRendererHealth");

app.MapGet("/api/tts/health",async (ITtsProvider tts, CancellationToken ct) =>
{
    var availability = await tts.CheckAvailabilityAsync(ct);
    return availability.IsReady
        ? Results.Ok(availability)
        : Results.Json(availability, statusCode: StatusCodes.Status503ServiceUnavailable);
})
    .WithName("GetTtsHealth");

app.MapGet("/api/assets",async (IAssetRepository assets, CancellationToken ct) =>
    Results.Ok(await assets.GetAllAsync(ct)))
    .WithName("GetAssets");

app.MapGet("/api/llm/health", async (ILlmProvider llm, CancellationToken ct) =>
{
    var availability = await llm.CheckAvailabilityAsync(ct);
    return availability is { IsReachable: true, IsModelAvailable: true }
        ? Results.Ok(availability)
        : Results.Json(availability, statusCode: StatusCodes.Status503ServiceUnavailable);
})
    .WithName("GetLlmHealth");

app.MapPost("/api/llm/ping", async (ILlmProvider llm, CancellationToken ct) =>
{
    try
    {
        var text = await llm.GenerateAsync("Reply with exactly one word: OK.", ct);
        return Results.Ok(new { response = text });
    }
    catch (LlmUnavailableException ex)
    {
        return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
})
    .WithName("PingLlm");

app.Run();

static Task<IResult> EnqueueOrConflictAsync(
    VideoJobScheduler scheduler, IVideoJobQueue queue, VideoJob job, VideoJobWorkKind kind, CancellationToken ct) =>
    QueueResults.ScheduleAsync(scheduler, queue, job, kind, ct);

public record HealthResponse(string Status, DateTimeOffset TimestampUtc);

public partial class Program
{
}
