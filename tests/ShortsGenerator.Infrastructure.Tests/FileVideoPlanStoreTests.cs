using Microsoft.Extensions.Options;
using ShortsGenerator.Domain;
using ShortsGenerator.Infrastructure.Storage;

namespace ShortsGenerator.Infrastructure.Tests;

public sealed class FileVideoPlanStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "plan-store-tests-" + Guid.NewGuid());
    private readonly JobOutputLocator _locator;
    private readonly FileVideoPlanStore _store;

    public FileVideoPlanStoreTests()
    {
        _locator = new JobOutputLocator(Options.Create(new OutputStorageOptions { RootDirectory = _root }));
        _store = new FileVideoPlanStore(_locator);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void JobDirectory_IsAlwaysDirectlyUnderTheOutputRoot()
    {
        var jobId = Guid.NewGuid();

        var directory = _locator.GetJobDirectory(jobId);

        Assert.Equal(Path.GetFullPath(_root), Path.GetDirectoryName(directory));
        Assert.Equal(jobId.ToString(), Path.GetFileName(directory));
    }

    [Fact]
    public void RelativeRoot_IsMadeAbsolute_SoTheWorkingDirectoryCantChangeWhereFilesGo()
    {
        var locator = new JobOutputLocator(Options.Create(new OutputStorageOptions { RootDirectory = "relative-output" }));

        Assert.True(Path.IsPathFullyQualified(locator.GetJobDirectory(Guid.NewGuid())));
    }

    [Fact]
    public async Task SaveThenLoad_RoundTripsThePlan_WithEnumsAsNames()
    {
        var jobId = Guid.NewGuid();
        var plan = Plan();

        var directory = await _store.SaveAsync(jobId, plan, CancellationToken.None);
        var loaded = await _store.LoadAsync(jobId, CancellationToken.None);

        Assert.Equal(_locator.GetJobDirectory(jobId), directory);
        Assert.NotNull(loaded);
        Assert.Equal(plan.Title, loaded.Title);
        Assert.Equal(SceneType.MythVsFact, loaded.Scenes[0].Type);
        Assert.Equal(["mascot-idle"], loaded.Scenes[0].AssetIds);
        // The renderer's Zod schema expects "MythVsFact", not 4.
        Assert.Contains("\"MythVsFact\"", await File.ReadAllTextAsync(Path.Combine(directory, "plan.json")));
    }

    [Fact]
    public async Task Save_OverwritesAtomically_LeavingNoTemporaryFileBehind()
    {
        var jobId = Guid.NewGuid();
        await _store.SaveAsync(jobId, Plan() with { Title = "First" }, CancellationToken.None);

        await _store.SaveAsync(jobId, Plan() with { Title = "Second" }, CancellationToken.None);

        Assert.Equal("Second", (await _store.LoadAsync(jobId, CancellationToken.None))!.Title);
        Assert.Equal(["plan.json"], Directory.GetFiles(_locator.GetJobDirectory(jobId)).Select(Path.GetFileName));
    }

    [Fact]
    public async Task Load_NoPlanYet_ReturnsNull()
    {
        Assert.Null(await _store.LoadAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Theory]
    [InlineData("{ \"title\": \"half a plan")]
    [InlineData("null")]
    [InlineData("")]
    public async Task Load_CorruptPlanFile_ThrowsAClearErrorNamingTheFile(string content)
    {
        var jobId = Guid.NewGuid();
        Directory.CreateDirectory(_locator.GetJobDirectory(jobId));
        await File.WriteAllTextAsync(Path.Combine(_locator.GetJobDirectory(jobId), "plan.json"), content);

        var ex = await Assert.ThrowsAsync<InvalidDataException>(() => _store.LoadAsync(jobId, CancellationToken.None));

        Assert.Contains("plan.json", ex.Message);
        Assert.Contains(jobId.ToString(), ex.Message);
    }

    private static VideoPlan Plan() => new(
        "Title", "Hook", 30, "Narration.",
        [new Scene("s1", 1, SceneType.MythVsFact, 5, "Narration.", "Text", ["mascot-idle"], AnimationId.Pop, SfxId.Pop, null, null)],
        "default", "bold", "upbeat");
}
