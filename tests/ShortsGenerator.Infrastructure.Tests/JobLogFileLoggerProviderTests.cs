using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ShortsGenerator.Infrastructure.Logging;
using ShortsGenerator.Infrastructure.Storage;

namespace ShortsGenerator.Infrastructure.Tests;

public sealed class JobLogFileLoggerProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "job-log-tests-" + Guid.NewGuid());
    private readonly JobOutputLocator _locator;
    private readonly ILoggerFactory _factory;

    public JobLogFileLoggerProviderTests()
    {
        _locator = new JobOutputLocator(Options.Create(new OutputStorageOptions { RootDirectory = _root }));
        // The real wiring: a LoggerFactory that hands its scopes to the provider.
        _factory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Trace)
            .AddProvider(new JobLogFileLoggerProvider(_locator)));
    }

    public void Dispose()
    {
        _factory.Dispose();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void EntriesInsideAJobScope_GoToThatJobsLog_IncludingFromOtherCategoriesAndExceptions()
    {
        var jobId = Guid.NewGuid();
        var pipeline = _factory.CreateLogger("ShortsGenerator.Application.VideoGenerationPipeline");
        var tts = _factory.CreateLogger("ShortsGenerator.Infrastructure.Tts.KokoroTtsProvider");

        using (pipeline.BeginScope(new Dictionary<string, object> { ["JobId"] = jobId }))
        {
            pipeline.LogInformation("Job {JobId}: running stage {Stage}", jobId, "Narration");
            tts.LogError(new InvalidOperationException("kokoro exploded"), "Synthesis failed");
        }

        var log = File.ReadAllText(Path.Combine(_locator.GetJobDirectory(jobId), JobLogFileLoggerProvider.FileName));
        Assert.Contains("INFORMATION VideoGenerationPipeline: Job " + jobId + ": running stage Narration", log);
        Assert.Contains("ERROR       KokoroTtsProvider: Synthesis failed", log);
        Assert.Contains("kokoro exploded", log);
    }

    [Fact]
    public void EntriesOutsideAnyJobScope_AndFrameworkChatter_AreNotWritten()
    {
        var jobId = Guid.NewGuid();
        _factory.CreateLogger("ShortsGenerator.Api").LogInformation("Now listening");
        var ef = _factory.CreateLogger("Microsoft.EntityFrameworkCore.Database.Command");
        using (ef.BeginScope(new Dictionary<string, object> { ["JobId"] = jobId }))
        {
            ef.LogInformation("Executed DbCommand (1ms) SELECT ...");
        }

        Assert.False(Directory.Exists(_root) && Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public void ConcurrentJobs_EachGetTheirOwnLog()
    {
        var ids = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToList();
        var logger = _factory.CreateLogger("ShortsGenerator.Application.VideoGenerationPipeline");

        Parallel.ForEach(ids, id =>
        {
            using (logger.BeginScope(new Dictionary<string, object> { ["JobId"] = id }))
            {
                for (var i = 0; i < 20; i++)
                {
                    logger.LogInformation("line {I} of {JobId}", i, id);
                }
            }
        });

        foreach (var id in ids)
        {
            var lines = File.ReadAllLines(Path.Combine(_locator.GetJobDirectory(id), JobLogFileLoggerProvider.FileName));
            Assert.Equal(20, lines.Length);
            Assert.All(lines, line => Assert.Contains(id.ToString(), line));
        }
    }
}
