using ShortsGenerator.Domain;

namespace ShortsGenerator.Application.Tests;

public sealed class RenderServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryVideoJobRepository _repository = new();
    private readonly FakeJobOutputLocator _locator = new();
    private readonly List<string> _createdDirectories = [];

    public void Dispose()
    {
        foreach (var directory in _createdDirectories)
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RenderAsync_NarratedJob_RendersIntoJobFolderAndCompletes()
    {
        var job = await AddNarratedJobAsync();
        var renderer = new FakeRenderer();

        var result = await CreateService(renderer).RenderAsync(job.Id, CancellationToken.None);

        var request = Assert.Single(renderer.Requests);
        var jobDirectory = _locator.GetJobDirectory(job.Id);
        Assert.Equal(Path.Combine(jobDirectory, "plan.json"), request.PlanPath);
        Assert.Equal(Path.Combine(jobDirectory, "narration.wav"), request.NarrationPath);
        Assert.Equal(Path.Combine(jobDirectory, "final.mp4"), request.OutputPath);
        Assert.Equal(VideoJobStatus.Completed, result.Status);
        Assert.Equal(request.OutputPath, result.FinalVideoPath);
    }

    [Fact]
    public async Task RenderAsync_CompletedJob_CanBeReRendered()
    {
        var job = await AddNarratedJobAsync();
        var renderer = new FakeRenderer();
        var service = CreateService(renderer);

        await service.RenderAsync(job.Id, CancellationToken.None);
        var again = await service.RenderAsync(job.Id, CancellationToken.None);

        Assert.Equal(2, renderer.Requests.Count);
        Assert.Equal(VideoJobStatus.Completed, again.Status);
    }

    [Fact]
    public async Task RenderAsync_NoNarrationYet_ThrowsInvalidOperationNamingTheMissingFile()
    {
        var job = await AddNarratedJobAsync(withNarrationFile: false);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService(new FakeRenderer()).RenderAsync(job.Id, CancellationToken.None));

        Assert.Contains("narration.wav", ex.Message);
    }

    [Fact]
    public async Task RenderAsync_JobStillPlanning_ThrowsInvalidOperation()
    {
        var job = VideoJob.Create("Pull-ups", null, Now);
        job.MarkPlanning(Now);
        await _repository.AddAsync(job, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService(new FakeRenderer()).RenderAsync(job.Id, CancellationToken.None));
    }

    [Fact]
    public async Task RenderAsync_RendererNotInstalled_LeavesJobUntouched()
    {
        var job = await AddNarratedJobAsync();

        var ex = await Assert.ThrowsAsync<RendererUnavailableException>(() =>
            CreateService(new FakeRenderer { IsReady = false }).RenderAsync(job.Id, CancellationToken.None));

        Assert.Contains("npm install", ex.Message);
        Assert.Equal(VideoJobStatus.Narrating, (await _repository.GetByIdAsync(job.Id, CancellationToken.None))!.Status);
    }

    [Fact]
    public async Task RenderAsync_RenderFails_MarksJobFailedWithReason()
    {
        var job = await AddNarratedJobAsync();
        var renderer = new FakeRenderer { Error = new RenderFailedException("chrome crashed") };

        await Assert.ThrowsAsync<RenderFailedException>(() =>
            CreateService(renderer).RenderAsync(job.Id, CancellationToken.None));

        var persisted = (await _repository.GetByIdAsync(job.Id, CancellationToken.None))!;
        Assert.Equal(VideoJobStatus.Failed, persisted.Status);
        Assert.Equal("chrome crashed", persisted.ErrorMessage);
    }

    [Fact]
    public async Task RenderAsync_Cancelled_DoesNotLeaveJobStuckInRendering()
    {
        var job = await AddNarratedJobAsync();
        var renderer = new FakeRenderer { Error = new OperationCanceledException() };

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            CreateService(renderer).RenderAsync(job.Id, CancellationToken.None));

        var persisted = (await _repository.GetByIdAsync(job.Id, CancellationToken.None))!;
        Assert.Equal(VideoJobStatus.Failed, persisted.Status);
        Assert.Contains("cancelled", persisted.ErrorMessage);
    }

    [Fact]
    public async Task RenderAsync_PlanWithEditedNarration_IsRefusedUntilTheVoiceOverIsRegenerated()
    {
        var job = await AddNarratedJobAsync();
        var planStore = new InMemoryVideoPlanStore();
        await planStore.SaveAsync(job.Id, new VideoPlan("T", "H", 30, "n", [], null, null, null, NarrationOutdated: true), CancellationToken.None);
        var renderer = new FakeRenderer();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RenderService(renderer, _repository, _locator, TimeProvider.System, planStore).RenderAsync(job.Id, CancellationToken.None));

        Assert.Contains("regenerate the narration", ex.Message);
        Assert.Empty(renderer.Requests);
    }

    [Fact]
    public async Task RenderAsync_PlanUsesAnAssetNoLongerInTheCatalog_IsRefusedBeforeRendering_NamingTheScene()
    {
        var job = await AddNarratedJobAsync();
        var planStore = new InMemoryVideoPlanStore();
        await planStore.SaveAsync(job.Id, PlanWithAssets("mascot-idle", "renamed-away"), CancellationToken.None);
        var renderer = new FakeRenderer();
        var service = new RenderService(renderer, _repository, _locator, TimeProvider.System, planStore, assets: Catalog("mascot-idle"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RenderAsync(job.Id, CancellationToken.None));

        Assert.Contains("s2", ex.Message);
        Assert.Contains("renamed-away", ex.Message);
        Assert.Empty(renderer.Requests);
        // Not Failed: the job stays editable, so the visual can be swapped with "Change".
        Assert.Equal(VideoJobStatus.Narrating, (await _repository.GetByIdAsync(job.Id, CancellationToken.None))!.Status);
    }

    [Fact]
    public async Task FindPlanProblemsAsync_KnownAssetsInAnyCase_HasNoProblems()
    {
        var job = await AddNarratedJobAsync();
        var planStore = new InMemoryVideoPlanStore();
        await planStore.SaveAsync(job.Id, PlanWithAssets("Mascot-Idle"), CancellationToken.None);
        var service = new RenderService(new FakeRenderer(), _repository, _locator, TimeProvider.System, planStore, assets: Catalog("mascot-idle"));

        Assert.Empty(await service.FindPlanProblemsAsync(job.Id, CancellationToken.None));
    }

    private static VideoPlan PlanWithAssets(params string[] assetIdPerScene) => new(
        "T", "H", 30, "n",
        assetIdPerScene.Select((id, i) => new Scene($"s{i + 1}", i + 1, SceneType.List, 5, "n", "x", [id], AnimationId.Pop, null, null, null)).ToList(),
        null, null, null);

    private static InMemoryAssetRepository Catalog(params string[] ids) => new(ids
        .Select(id => new AssetMetadata(id, $"assets/{id}.svg", "test", [], [], AssetOrientation.Square, null))
        .ToArray());

    private RenderService CreateService(FakeRenderer renderer) =>
        new(renderer, _repository, _locator, TimeProvider.System);

    private async Task<VideoJob> AddNarratedJobAsync(bool withNarrationFile = true)
    {
        var job = VideoJob.Create("Pull-ups", null, Now);
        job.MarkPlanning(Now);
        job.AttachGeneratedScript("script", Now);
        job.MarkNarrating(Now);
        await _repository.AddAsync(job, CancellationToken.None);

        var directory = _locator.GetJobDirectory(job.Id);
        Directory.CreateDirectory(directory);
        _createdDirectories.Add(directory);
        File.WriteAllText(Path.Combine(directory, "plan.json"), "{}");
        if (withNarrationFile)
        {
            File.WriteAllBytes(Path.Combine(directory, "narration.wav"), [1]);
        }

        return job;
    }
}
