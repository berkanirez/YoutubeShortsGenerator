using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShortsGenerator.Application;

namespace ShortsGenerator.Infrastructure.Jobs;

// Day 26: resumes work the previous API process didn't finish. An IHostedService's StartAsync
// runs once, after Program.cs applied the migrations and before the server takes requests, so
// nothing else can be queuing work at the same time.
public sealed class JobRecoveryHostedService : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<JobRecoveryHostedService> _logger;

    public JobRecoveryHostedService(IServiceScopeFactory scopeFactory, ILogger<JobRecoveryHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var report = await scope.ServiceProvider.GetRequiredService<JobRecoveryService>().RecoverInterruptedAsync(cancellationToken);
            if (report.Resumed.Count + report.GaveUp.Count > 0)
            {
                _logger.LogInformation(
                    "Startup recovery: resumed {Resumed} interrupted job(s), gave up on {GaveUp}",
                    report.Resumed.Count, report.GaveUp.Count);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Recovery is a convenience: the API must still start, and every job can still
            // be continued by hand from the dashboard.
            _logger.LogError(ex, "Startup recovery of interrupted jobs failed");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
