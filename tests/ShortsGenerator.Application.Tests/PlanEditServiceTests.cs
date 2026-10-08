using ShortsGenerator.Domain;

namespace ShortsGenerator.Application.Tests;

public class PlanEditServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryVideoJobRepository _repository = new();
    private readonly InMemoryVideoPlanStore _planStore = new();
    private readonly FakeQueue _queue = new();
    private readonly InMemoryAssetRepository _assets = new(
        Asset("mascot-idle"), Asset("core-highlight"), Asset("biceps-highlight"), Asset("dumbbell"));

    [Fact]
    public async Task ChangeSceneAsset_SwapsTheVisualAndSavesTheValidatedPlan()
    {
        var job = await AddCompletedJobAsync();

        var plan = await Service().ChangeSceneAssetAsync(job.Id, "s2", "core-highlight", CancellationToken.None);

        Assert.Equal(["core-highlight"], plan.Scenes.Single(s => s.Id == "s2").AssetIds);
        Assert.Same(plan, await _planStore.LoadAsync(job.Id, CancellationToken.None));
        Assert.Equal(VideoJobStatus.Completed, (await _repository.GetByIdAsync(job.Id, CancellationToken.None))!.Status);
    }

    [Fact]
    public async Task ChangeSceneAsset_WorksOnAudioTimedPlansShorterThanTheLlmDurationLimits()
    {
        // 6 scenes x 2.8s = 16.8s: below the 20s minimum meant for LLM guesses, but it's the
        // real measured voice-over, so it must stay editable.
        var job = await AddCompletedJobAsync(sceneSeconds: 2.8);

        var plan = await Service().ChangeSceneAssetAsync(job.Id, "s3", "dumbbell", CancellationToken.None);

        Assert.Equal(["dumbbell"], plan.Scenes.Single(s => s.Id == "s3").AssetIds);
    }

    [Fact]
    public async Task ChangeSceneAsset_UnknownAsset_IsRejected()
    {
        var job = await AddCompletedJobAsync();

        var ex = await Assert.ThrowsAsync<PlanEditException>(() =>
            Service().ChangeSceneAssetAsync(job.Id, "s2", "../../evil", CancellationToken.None));

        Assert.Contains("not an asset in the catalog", ex.Message);
    }

    [Fact]
    public async Task ChangeSceneAsset_UnknownScene_IsNotFound()
    {
        var job = await AddCompletedJobAsync();

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            Service().ChangeSceneAssetAsync(job.Id, "s99", "dumbbell", CancellationToken.None));
    }

    [Fact]
    public async Task ChangeSceneAsset_WhileQueuedOrRendering_IsRefused()
    {
        var queued = await AddCompletedJobAsync();
        _queue.Queued.Add(queued.Id);
        var rendering = await AddCompletedJobAsync();
        rendering.MarkRendering(Now);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service().ChangeSceneAssetAsync(queued.Id, "s2", "dumbbell", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service().ChangeSceneAssetAsync(rendering.Id, "s2", "dumbbell", CancellationToken.None));
    }

    [Fact]
    public async Task ChangeSceneAsset_RecomputesVarietyWarningsForTheReviewer()
    {
        var job = await AddCompletedJobAsync();
        var service = Service();

        await service.ChangeSceneAssetAsync(job.Id, "s2", "dumbbell", CancellationToken.None);
        var plan = await service.ChangeSceneAssetAsync(job.Id, "s3", "dumbbell", CancellationToken.None);

        Assert.Contains(plan.ReviewWarnings!, w => w.Contains("both show 'dumbbell'"));
    }

    [Fact]
    public async Task UpdateScene_EditsVisualFields_WithNullMeaningKeepAndEmptyMeaningClear()
    {
        var job = await AddCompletedJobAsync();
        var service = Service();
        await service.UpdateSceneAsync(job.Id, "s3", new SceneEdit(OnScreenText: "Old", Emphasis: "Sentence", Sfx: SfxId.Pop), CancellationToken.None);

        var plan = await service.UpdateSceneAsync(
            job.Id, "s3", new SceneEdit(OnScreenText: "  Lats do the work ", Animation: AnimationId.Bounce, Emphasis: "", ClearSfx: true, Items: ["A", " ", "B"]),
            CancellationToken.None);

        var scene = plan.Scenes.Single(s => s.Id == "s3");
        Assert.Equal("Lats do the work", scene.OnScreenText);
        Assert.Equal(AnimationId.Bounce, scene.Animation);
        Assert.Null(scene.Emphasis);
        Assert.Null(scene.Sfx);
        Assert.Equal(["A", "B"], scene.Items);
        Assert.False(plan.NarrationOutdated);
    }

    [Fact]
    public async Task UpdateScene_NarrationEdit_RebuildsTheFullTextAndMarksTheVoiceOverOutdated()
    {
        var job = await AddCompletedJobAsync();

        var plan = await Service().UpdateSceneAsync(job.Id, "s2", new SceneEdit(Narration: "A clearer second sentence."), CancellationToken.None);

        Assert.True(plan.NarrationOutdated);
        Assert.Equal("Sentence 1. A clearer second sentence. Sentence 3. Sentence 4. Sentence 5. Sentence 6.", plan.NarrationText);
    }

    [Fact]
    public async Task UpdateScene_UnsafeNarration_IsRejectedLikeAGeneratedScript()
    {
        var job = await AddCompletedJobAsync();

        var ex = await Assert.ThrowsAsync<PlanEditException>(() =>
            Service().UpdateSceneAsync(job.Id, "s2", new SceneEdit(Narration: "This routine will cure your back pain."), CancellationToken.None));

        Assert.Contains("cure", ex.Message);
    }

    [Fact]
    public async Task UpdateScene_HookAndOutroCantBeRetyped_NorCreated()
    {
        var job = await AddCompletedJobAsync();
        var service = Service();

        await Assert.ThrowsAsync<PlanEditException>(() =>
            service.UpdateSceneAsync(job.Id, "s1", new SceneEdit(Type: SceneType.List), CancellationToken.None));
        await Assert.ThrowsAsync<PlanEditException>(() =>
            service.UpdateSceneAsync(job.Id, "s3", new SceneEdit(Type: SceneType.Outro), CancellationToken.None));
        var plan = await service.UpdateSceneAsync(job.Id, "s3", new SceneEdit(Type: SceneType.MythVsFact), CancellationToken.None);
        Assert.Equal(SceneType.MythVsFact, plan.Scenes.Single(s => s.Id == "s3").Type);
    }

    [Fact]
    public async Task UpdateScene_EmptyNarrationOnANormalScene_FailsValidation()
    {
        var job = await AddCompletedJobAsync();

        await Assert.ThrowsAsync<PlanEditException>(() =>
            Service().UpdateSceneAsync(job.Id, "s2", new SceneEdit(Narration: " "), CancellationToken.None));
    }

    [Fact]
    public async Task UpdateSettings_OnlyKnownCaptionStylesAndMoods()
    {
        var job = await AddCompletedJobAsync();
        var service = Service();

        var plan = await service.UpdateSettingsAsync(job.Id, new PlanSettingsEdit("Boxed", "calm"), CancellationToken.None);
        Assert.Equal("boxed", plan.CaptionStyle);
        Assert.Equal("calm", plan.MusicMood);

        await Assert.ThrowsAsync<PlanEditException>(() =>
            service.UpdateSettingsAsync(job.Id, new PlanSettingsEdit(CaptionStyle: "comic-sans"), CancellationToken.None));
        await Assert.ThrowsAsync<PlanEditException>(() =>
            service.UpdateSettingsAsync(job.Id, new PlanSettingsEdit(MusicMood: "epic"), CancellationToken.None));
    }

    private PlanEditService Service() => new(_repository, _planStore, _assets, _queue);

    private async Task<VideoJob> AddCompletedJobAsync(double sceneSeconds = 5)
    {
        var job = VideoJob.Create("Core", null, Now);
        job.MarkPlanning(Now);
        job.AttachGeneratedScript("script", Now);
        job.MarkNarrating(Now);
        job.MarkRendering(Now);
        job.MarkCompleted("final.mp4", Now);
        await _repository.AddAsync(job, CancellationToken.None);

        SceneType[] types = [SceneType.Hook, SceneType.ExerciseDemo, SceneType.AnatomyHighlight, SceneType.List, SceneType.Progress, SceneType.Outro];
        var scenes = types.Select((type, i) => new Scene(
            $"s{i + 1}", i + 1, type, sceneSeconds, $"Sentence {i + 1}.", null, [], AnimationId.Fade, null, null, null)).ToList();
        var narration = string.Join(' ', scenes.Select(s => s.Narration));
        await _planStore.SaveAsync(job.Id, new VideoPlan("T", "H", scenes.Sum(s => s.DurationSeconds), narration, scenes, null, null, null), CancellationToken.None);
        return job;
    }

    private static AssetMetadata Asset(string id) =>
        new(id, $"../x/{id}.svg", "icon", [id], [SceneType.List], AssetOrientation.Square, null);

    private sealed class FakeQueue : IVideoJobQueue
    {
        public HashSet<Guid> Queued { get; } = [];
        public bool TryEnqueue(VideoJobWorkItem item) => Queued.Add(item.JobId);
        public ValueTask<VideoJobWorkItem> DequeueAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public bool IsQueued(Guid jobId) => Queued.Contains(jobId);
    }
}
