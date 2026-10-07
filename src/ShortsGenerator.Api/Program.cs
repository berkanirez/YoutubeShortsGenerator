using Microsoft.EntityFrameworkCore;
using ShortsGenerator.Api.Contracts;
using ShortsGenerator.Application;
using ShortsGenerator.Domain;
using ShortsGenerator.Infrastructure;
using ShortsGenerator.Infrastructure.Llm;
using ShortsGenerator.Infrastructure.Storage;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddOllamaLlmProvider(builder.Configuration);
builder.Services.AddVideoPlanStorage(builder.Configuration);
builder.Services.AddScoped<VideoJobService>();
builder.Services.AddScoped<ScriptGenerationService>();
builder.Services.AddScoped<ScenePlanningService>();

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
}

app.UseHttpsRedirection();

app.MapGet("/api/health", () => Results.Ok(new HealthResponse("ok", DateTimeOffset.UtcNow)))
    .WithName("GetHealth");

app.MapPost("/api/videos", async (CreateVideoJobRequest request, VideoJobService service, CancellationToken ct) =>
{
    try
    {
        var job = await service.CreateJobAsync(request.Topic, request.SourceScript, ct);
        return Results.Created($"/api/videos/{job.Id}", VideoJobResponse.FromDomain(job));
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
})
    .WithName("CreateVideoJob");

app.MapGet("/api/videos/{id:guid}", async (Guid id, VideoJobService service, CancellationToken ct) =>
{
    var job = await service.GetJobAsync(id, ct);
    return job is null ? Results.NotFound() : Results.Ok(VideoJobResponse.FromDomain(job));
})
    .WithName("GetVideoJob");

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

public record HealthResponse(string Status, DateTimeOffset TimestampUtc);

public partial class Program
{
}
