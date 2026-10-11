using ShortsGenerator.Application;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Api.Endpoints;

public sealed record ArtRequestResponse(
    string FileName,
    string Subject,
    string Kind,
    IReadOnlyList<string> SceneIds,
    string Prompt,
    bool NeedsReferenceImage,
    bool Supplied);

public sealed record ArtContinueResponse(bool Resumed, IReadOnlyList<string> Imported, IReadOnlyList<string> Missing);

// The "pictures needed" loop for a job in WaitingForArt (ArtRequestService): see the prompts,
// upload each generated image, continue.
public static class ArtEndpoints
{
    public static IEndpointRouteBuilder MapArtEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/videos/{id:guid}/art", async (Guid id, VideoJobService jobs, ArtRequestService art, CancellationToken ct) =>
        {
            if (await jobs.GetJobAsync(id, ct) is null)
            {
                return Results.NotFound();
            }

            var requests = await art.GetRequestsAsync(id, ct);
            return Results.Ok(requests.Select(r => new ArtRequestResponse(
                r.Subject.IncomingFileName,
                r.Subject.Id,
                r.Subject.Kind.ToString(),
                r.SceneIds,
                r.Prompt,
                ArtPromptBuilder.NeedsReferenceImage(r.Subject),
                art.HasSource(r))));
        })
            .WithName("GetArtRequests");

        // The image as the raw request body (what the dashboard's file picker sends). The file
        // name must be one the job asked for, so nothing else can be written.
        app.MapPut("/api/videos/{id:guid}/art/{fileName}", async (
            Guid id, string fileName, HttpRequest request, ArtRequestService art, CancellationToken ct) =>
        {
            try
            {
                await art.SaveUploadAsync(id, fileName, request.Body, ct);
                return Results.NoContent();
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (ArtImportException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
            .WithName("UploadArt");

        app.MapPost("/api/videos/{id:guid}/art/continue", async (
            Guid id, bool? skipMissing, ArtRequestService art, VideoJobService jobs,
            VideoJobScheduler scheduler, IVideoJobQueue queue, CancellationToken ct) =>
        {
            ArtContinueResult result;
            try
            {
                result = await art.ContinueAsync(id, skipMissing ?? false, ct);
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
            catch (ArtImportException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            var response = new ArtContinueResponse(
                result.Resumed, result.Imported, result.Missing.Select(r => r.Subject.IncomingFileName).ToList());
            if (!result.Resumed)
            {
                return Results.Ok(response);
            }

            var job = await jobs.GetJobAsync(id, ct);
            var scheduled = await QueueResults.ScheduleAsync(scheduler, queue, job!, VideoJobWorkKind.Generate, ct);
            return scheduled is Microsoft.AspNetCore.Http.HttpResults.Accepted<Contracts.VideoJobResponse>
                ? Results.Accepted($"/api/videos/{id}", response)
                : scheduled;
        })
            .WithName("ContinueAfterArt");

        return app;
    }
}
