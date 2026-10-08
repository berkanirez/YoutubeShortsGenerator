using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShortsGenerator.Application;

namespace ShortsGenerator.Infrastructure.Jobs;

// The single consumer of IVideoJobQueue: one job at a time, by design (CLAUDE.md section 15).
// Rendering is CPU-heavy and two renders would race on renderer/public/assets anyway.
public sealed class VideoJobWorker : BackgroundService
{
    private readonly IVideoJobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<VideoJobWorker> _logger;

    public VideoJobWorker(IVideoJobQueue queue, IServiceScopeFactory scopeFactory, ILogger<VideoJobWorker> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>The job currently being processed, if any (for status display).</summary>
    public Guid? CurrentJobId { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Video job worker started");
        while (!stoppingToken.IsCancellationRequested)
        {
            VideoJobWorkItem item;
            try
            {
                item = await _queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            CurrentJobId = item.JobId;
            try
            {
                // A fresh DI scope per job: the services it uses (DbContext, repositories)
                // are scoped, i.e. meant to live for one unit of work - exactly like one HTTP
                // request. A BackgroundService itself is a singleton, so it must never hold
                // a scoped service directly.
                await using var scope = _scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<IVideoJobProcessor>();
                await processor.ProcessAsync(item, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // The processor records job failures itself; anything reaching here is a bug.
                // Log it and keep the loop alive - one bad job must not stop every later one.
                _logger.LogError(ex, "Worker failed while processing job {JobId}", item.JobId);
            }
            finally
            {
                CurrentJobId = null;
            }
        }

        _logger.LogInformation("Video job worker stopped");
    }
}
