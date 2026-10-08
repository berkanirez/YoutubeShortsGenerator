using ShortsGenerator.Domain;

namespace ShortsGenerator.Application.Tests;

public class NarrationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    private const string Narration = "Keep your core tight. Pull your chest to the bar.";

    private readonly InMemoryVideoJobRepository _repository = new();
    private readonly InMemoryVideoPlanStore _planStore = new();
    private readonly FakeJobOutputLocator _locator = new();

    [Fact]
    public async Task GenerateNarrationAsync_PlannedJob_SendsOneSegmentPerNarratedSceneIntoJobFolderAndMarksNarrating()
    {
        var job = await AddPlannedJobAsync();
        var tts = new FakeTtsProvider();

        await CreateService(tts).GenerateNarrationAsync(job.Id, CancellationToken.None);

        var request = Assert.Single(tts.Requests);
        Assert.Equal(["Keep your core tight.", "Pull your chest to the bar."], request.Segments);
        Assert.Equal(Path.Combine(_locator.GetJobDirectory(job.Id), "narration.wav"), request.OutputPath);
        Assert.Equal(VideoJobStatus.Narrating, (await _repository.GetByIdAsync(job.Id, CancellationToken.None))!.Status);
    }

    [Fact]
    public async Task GenerateNarrationAsync_SavesAPlanTimedFromTheAudio()
    {
        var job = await AddPlannedJobAsync();
        var tts = new FakeTtsProvider { SegmentSeconds = 2.0, PauseSeconds = 0.3 };

        var outcome = await CreateService(tts).GenerateNarrationAsync(job.Id, CancellationToken.None);

        // Planned: 5s + 5s + 3s silent outro. Audio: 2.3s + 2.3s, then the outro.
        Assert.Equal([2.3, 2.3, 3.0], outcome.TimedPlan.Scenes.Select(s => Math.Round(s.DurationSeconds, 6)));
        Assert.Equal([2.0, 2.0], outcome.TimedPlan.Scenes.Take(2).Select(s => s.SpokenDurationSeconds!.Value));
        Assert.Equal(7.6, outcome.TimedPlan.TargetDurationSeconds, precision: 6);

        var saved = await _planStore.LoadAsync(job.Id, CancellationToken.None);
        Assert.Same(outcome.TimedPlan, saved);
    }

    [Fact]
    public async Task GenerateNarrationAsync_AlreadyNarrating_CanRegenerate()
    {
        var job = await AddPlannedJobAsync();
        var tts = new FakeTtsProvider();
        var service = CreateService(tts);

        await service.GenerateNarrationAsync(job.Id, CancellationToken.None);
        await service.GenerateNarrationAsync(job.Id, CancellationToken.None);

        Assert.Equal(2, tts.Requests.Count);
    }

    [Fact]
    public async Task GenerateNarrationAsync_NoPlanYet_ThrowsInvalidOperation()
    {
        var job = VideoJob.Create("Pull-ups", null, Now);
        job.MarkPlanning(Now);
        await _repository.AddAsync(job, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateService(new FakeTtsProvider()).GenerateNarrationAsync(job.Id, CancellationToken.None));
        Assert.Contains("plan", ex.Message);
    }

    [Fact]
    public async Task GenerateNarrationAsync_UnknownJob_ThrowsKeyNotFound()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            CreateService(new FakeTtsProvider()).GenerateNarrationAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task GenerateNarrationAsync_EngineNotInstalled_ExplainsHowToFixAndLeavesJobUntouched()
    {
        var job = await AddPlannedJobAsync();

        var ex = await Assert.ThrowsAsync<TtsUnavailableException>(() =>
            CreateService(new FakeTtsProvider { IsReady = false }).GenerateNarrationAsync(job.Id, CancellationToken.None));

        Assert.Contains("fake engine missing", ex.Message);
        Assert.Contains("Run install-fake.ps1", ex.Message);
        Assert.Equal(VideoJobStatus.Planning, (await _repository.GetByIdAsync(job.Id, CancellationToken.None))!.Status);
    }

    [Fact]
    public async Task GenerateNarrationAsync_SynthesisFails_MarksJobFailedWithReason()
    {
        var job = await AddPlannedJobAsync();
        var tts = new FakeTtsProvider { SynthesisError = new TtsGenerationException("engine crashed") };

        await Assert.ThrowsAsync<TtsGenerationException>(() =>
            CreateService(tts).GenerateNarrationAsync(job.Id, CancellationToken.None));

        var persisted = (await _repository.GetByIdAsync(job.Id, CancellationToken.None))!;
        Assert.Equal(VideoJobStatus.Failed, persisted.Status);
        Assert.Equal("engine crashed", persisted.ErrorMessage);
    }

    private NarrationService CreateService(FakeTtsProvider tts) =>
        new(tts, _repository, _planStore, _locator, TimeProvider.System);

    private async Task<VideoJob> AddPlannedJobAsync()
    {
        var job = VideoJob.Create("Pull-ups", null, Now);
        job.MarkPlanning(Now);
        job.AttachGeneratedScript(Narration, Now);
        await _repository.AddAsync(job, CancellationToken.None);

        Scene[] scenes =
        [
            new("s1", 1, SceneType.Hook, 5, "Keep your core tight.", null, [], AnimationId.Fade, null, null, null),
            new("s2", 2, SceneType.ExerciseDemo, 5, "Pull your chest to the bar.", null, [], AnimationId.Slide, null, null, null),
            new("s3", 3, SceneType.Outro, 3, null, "Follow", [], AnimationId.Pop, null, null, null),
        ];
        await _planStore.SaveAsync(job.Id, new VideoPlan("Title", "Hook", 13, Narration, scenes, null, null, null), CancellationToken.None);
        return job;
    }
}
