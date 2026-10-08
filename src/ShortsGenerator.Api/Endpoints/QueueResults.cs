using ShortsGenerator.Api.Contracts;
using ShortsGenerator.Application;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Api.Endpoints;

// Every endpoint that queues work goes through VideoJobScheduler (Day 26), so the intent is
// persisted and survives an API restart; this maps its outcome to HTTP in one place.
public static class QueueResults
{
    public static async Task<IResult> ScheduleAsync(
        VideoJobScheduler scheduler, IVideoJobQueue queue, VideoJob job, VideoJobWorkKind kind, CancellationToken ct) =>
        await scheduler.ScheduleAsync(job, kind, ct) switch
        {
            ScheduleResult.Queued => Results.Accepted($"/api/videos/{job.Id}", VideoJobResponse.FromDomain(job, queue)),
            ScheduleResult.AlreadyQueued => Results.Conflict(new { error = $"Video job '{job.Id}' is already queued." }),
            _ => Results.Json(
                new { error = "The local job queue is full. Wait for some jobs to finish and try again." },
                statusCode: StatusCodes.Status503ServiceUnavailable),
        };
}
