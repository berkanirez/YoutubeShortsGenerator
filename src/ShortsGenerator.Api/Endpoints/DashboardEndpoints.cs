using System.Diagnostics;
using Microsoft.Extensions.FileProviders;
using ShortsGenerator.Api.Contracts;
using ShortsGenerator.Application;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Api.Endpoints;

// The local dashboard (web/): served as static files by this same process, plus the few
// endpoints only a UI needs - watching the video, reading its review checklist, opening
// its folder. Everything stays on localhost; nothing here talks to the internet.
public static class DashboardEndpoints
{
    public const string SectionName = "Dashboard";

    public static WebApplication UseDashboard(this WebApplication app)
    {
        // Relative to the Api content root like every other configured path (Day 11):
        // the repository's web/ folder.
        var directory = Path.GetFullPath(
            app.Configuration[$"{SectionName}:Directory"] ?? "../../web", app.Environment.ContentRootPath);
        if (!Directory.Exists(directory))
        {
            app.Logger.LogWarning("Dashboard folder {Directory} not found; the UI is disabled", directory);
            return app;
        }

        var files = new PhysicalFileProvider(directory);
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
        app.UseStaticFiles(new StaticFileOptions { FileProvider = files });
        return app;
    }

    public static WebApplication MapDashboardEndpoints(this WebApplication app)
    {
        // Asset preview for the picker. Shown with <img> (which never runs scripts inside an
        // SVG), and served with a CSP that forbids scripts even if the file is opened directly -
        // on top of the Day 20 rule that catalog SVGs can't contain any.
        app.MapGet("/api/assets/{id}/svg", async (string id, IAssetRepository assets, HttpContext context, CancellationToken ct) =>
        {
            var asset = await assets.GetByIdAsync(id, ct);
            if (asset is null || !File.Exists(asset.FilePath))
            {
                return Results.NotFound();
            }

            context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            return Results.File(asset.FilePath, "image/svg+xml");
        })
            .WithName("GetAssetSvg");

        // Swap one scene's visual for another catalog asset (Day 22). The plan is re-validated;
        // the job's video is marked outdated until it's re-rendered.
        app.MapPut("/api/videos/{id:guid}/scenes/{sceneId}/asset", async (
            Guid id, string sceneId, ChangeSceneAssetRequest request, PlanEditService edits, CancellationToken ct) =>
            await EditAsync(() => edits.ChangeSceneAssetAsync(id, sceneId, request.AssetId, ct)))
            .WithName("ChangeSceneAsset");

        // Day 23: edit permitted scene fields / plan settings, then apply.
        app.MapPut("/api/videos/{id:guid}/scenes/{sceneId}", async (
            Guid id, string sceneId, SceneEditRequest request, PlanEditService edits, CancellationToken ct) =>
            await EditAsync(() => edits.UpdateSceneAsync(id, sceneId, request.ToDomain(), ct)))
            .WithName("EditScene");

        app.MapPut("/api/videos/{id:guid}/settings", async (
            Guid id, PlanSettingsRequest request, PlanEditService edits, CancellationToken ct) =>
            await EditAsync(() => edits.UpdateSettingsAsync(id, new PlanSettingsEdit(request.CaptionStyle, request.MusicMood), ct)))
            .WithName("EditPlanSettings");

        // One button for the user: re-render if only visuals/text changed, regenerate the
        // voice-over first if narration text changed. The plan knows which (NarrationOutdated).
        app.MapPost("/api/videos/{id:guid}/apply-edits", async (
            Guid id, VideoJobService jobs, IVideoPlanStore plans, RenderService renders, VideoJobScheduler scheduler,
            IVideoJobQueue queue, CancellationToken ct) =>
        {
            var job = await jobs.GetJobAsync(id, ct);
            if (job is null)
            {
                return Results.NotFound();
            }

            if (job.Status is not (VideoJobStatus.Narrating or VideoJobStatus.Completed) || job.PendingWork is not null)
            {
                return Results.Conflict(new { error = $"Video job '{id}' is '{job.Status}'; edits can be applied once it has narration and isn't being processed." });
            }

            var problems = await renders.FindPlanProblemsAsync(id, ct);
            if (problems.Count > 0)
            {
                return Results.Conflict(new { error = string.Join(" ", problems) });
            }

            var plan = await plans.LoadAsync(id, ct);
            var kind = plan?.NarrationOutdated == true ? VideoJobWorkKind.Renarrate : VideoJobWorkKind.Render;
            return await QueueResults.ScheduleAsync(scheduler, queue, job, kind, ct);
        })
            .WithName("ApplyPlanEdits");

        // enableRangeProcessing: the browser's <video> asks for byte ranges to seek and to
        // start playing before the whole file has downloaded (HTTP 206 Partial Content).
        app.MapGet("/api/videos/{id:guid}/video", (Guid id, IJobOutputLocator locator) =>
        {
            var path = Path.Combine(locator.GetJobDirectory(id), RenderService.FinalVideoFileName);
            return File.Exists(path)
                ? Results.File(path, "video/mp4", enableRangeProcessing: true)
                : Results.NotFound();
        })
            .WithName("GetVideoFile");

        app.MapGet("/api/videos/{id:guid}/review", (Guid id, IJobOutputLocator locator) =>
        {
            var path = Path.Combine(locator.GetJobDirectory(id), ReviewChecklist.FileName);
            return File.Exists(path)
                ? Results.File(path, "text/markdown; charset=utf-8")
                : Results.NotFound();
        })
            .WithName("GetVideoReview");

        // Opens the job's folder in the OS file manager - this app runs on the user's own
        // machine, so "show me the file" is a legitimate thing for the API to do. The folder
        // comes from the job's Guid (never from request text) and explorer.exe is started
        // directly with ArgumentList - no shell.
        app.MapPost("/api/videos/{id:guid}/open-folder", (Guid id, IJobOutputLocator locator, HttpContext context) =>
        {
            if (!IsLocalRequest(context))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var directory = locator.GetJobDirectory(id);
            if (!Directory.Exists(directory))
            {
                return Results.NotFound();
            }

            if (!OperatingSystem.IsWindows())
            {
                return Results.Ok(new { path = directory, opened = false });
            }

            var startInfo = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
            startInfo.ArgumentList.Add(directory);
            Process.Start(startInfo)?.Dispose();
            return Results.Ok(new { path = directory, opened = true });
        })
            .WithName("OpenVideoFolder");

        return app;
    }

    // A Completed job whose plan.json was edited after final.mp4 was written: what you'd see
    // in the player is no longer what the plan says.
    public static bool IsVideoOutdated(IJobOutputLocator locator, Guid jobId)
    {
        var directory = locator.GetJobDirectory(jobId);
        var video = new FileInfo(Path.Combine(directory, RenderService.FinalVideoFileName));
        var plan = new FileInfo(Path.Combine(directory, RenderService.PlanFileName));
        return video.Exists && plan.Exists && plan.LastWriteTimeUtc > video.LastWriteTimeUtc;
    }

    // PlanEditException is caught first: it's also what SceneEditRequest.ToDomain throws for
    // an unknown animation/type name, which happens inside the edit delegate.
    internal static async Task<IResult> EditAsync(Func<Task<VideoPlan>> edit)
    {
        try
        {
            return Results.Ok(await edit());
        }
        catch (PlanEditException ex)
        {
            return Results.UnprocessableEntity(new { error = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return Results.NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(new { error = ex.Message });
        }
    }

    private static bool IsLocalRequest(HttpContext context) =>
        context.Connection.RemoteIpAddress is { } address && System.Net.IPAddress.IsLoopback(address);
}
