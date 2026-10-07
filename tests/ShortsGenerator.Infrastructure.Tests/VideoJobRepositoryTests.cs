using Microsoft.EntityFrameworkCore;
using ShortsGenerator.Domain;
using ShortsGenerator.Infrastructure;

namespace ShortsGenerator.Infrastructure.Tests;

public class VideoJobRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly ShortsGeneratorDbContext _dbContext;
    private readonly VideoJobRepository _repository;

    public VideoJobRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"shortsgenerator-tests-{Guid.NewGuid()}.db");
        var options = new DbContextOptionsBuilder<ShortsGeneratorDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;

        _dbContext = new ShortsGeneratorDbContext(options);
        _dbContext.Database.Migrate();
        _repository = new VideoJobRepository(_dbContext);
    }

    [Fact]
    public async Task AddAsync_ThenGetByIdAsync_ReturnsEquivalentJob()
    {
        var now = DateTimeOffset.UtcNow;
        var job = VideoJob.Create("Why Your Pull-Ups Aren't Increasing", "optional script", now);

        await _repository.AddAsync(job, CancellationToken.None);

        // A fresh DbContext proves the data actually round-tripped through SQLite,
        // not just EF Core's in-memory change tracker.
        await using var freshDbContext = new ShortsGeneratorDbContext(
            new DbContextOptionsBuilder<ShortsGeneratorDbContext>().UseSqlite($"Data Source={_dbPath}").Options);
        var freshRepository = new VideoJobRepository(freshDbContext);

        var loaded = await freshRepository.GetByIdAsync(job.Id, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(job.Id, loaded!.Id);
        Assert.Equal(job.Topic, loaded.Topic);
        Assert.Equal(job.SourceScript, loaded.SourceScript);
        Assert.Equal(VideoJobStatus.Created, loaded.Status);
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        var result = await _repository.GetByIdAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task ListRecentAsync_ReturnsNewestFirst()
    {
        var older = VideoJob.Create("Older topic", null, DateTimeOffset.UtcNow.AddMinutes(-10));
        var newer = VideoJob.Create("Newer topic", null, DateTimeOffset.UtcNow);
        await _repository.AddAsync(older, CancellationToken.None);
        await _repository.AddAsync(newer, CancellationToken.None);

        var recent = await _repository.ListRecentAsync(10, CancellationToken.None);

        Assert.Equal(newer.Id, recent[0].Id);
        Assert.Equal(older.Id, recent[1].Id);
    }

    public void Dispose()
    {
        _dbContext.Dispose();

        // Microsoft.Data.Sqlite pools connections by default; without clearing the
        // pool, the OS keeps the file handle open and deleting it fails on Windows.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();

        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }
}
