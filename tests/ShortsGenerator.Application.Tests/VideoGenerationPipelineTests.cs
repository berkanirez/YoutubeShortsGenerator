using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Application.Tests;

// Runs the real stage services end to end, with only the external engines (LLM, TTS,
// renderer) faked - this is the pipeline the background worker executes.
public sealed class VideoGenerationPipelineTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string Narration = TestNarrations.PullUps;

    private readonly InMemoryVideoJobRepository _repository = new();
    private readonly TempJobOutputLocator _locator = new();
    private readonly InMemoryVideoPlanStore _planStore;

    public VideoGenerationPipelineTests()
    {
        _planStore = new InMemoryVideoPlanStore(_locator);
    }

    public void Dispose() => _locator.Dispose();

    [Fact]
    public async Task ProcessAsync_NewJob_RunsEveryStageToCompleted()
    {
        var job = await AddJobAsync();
        var llm = new FakeLlmProvider(Narration, BuildValidPlanJson());
        var tts = new FakeTtsProvider { WriteOutputFile = true };
        var renderer = new FakeRenderer();

        await CreatePipeline(llm, tts, renderer).ProcessAsync(Generate(job), CancellationToken.None);

        var done = await GetAsync(job);
        Assert.Equal(VideoJobStatus.Completed, done.Status);
        Assert.Equal(Narration, done.GeneratedScript);
        Assert.EndsWith("final.mp4", done.FinalVideoPath);
        Assert.Equal(2, llm.ReceivedPrompts.Count); // script + plan
        Assert.Single(tts.Requests);
        Assert.Single(renderer.Requests);
    }

    [Fact]
    public async Task ProcessAsync_JobAlreadyNarrated_ResumesWithOnlyTheRender()
    {
        // Drive the first three stages "by hand" (as the manual endpoints would), then hand
        // the job to the pipeline: it must only render.
        var resumeJob = await AddJobAsync();
        var resumeLlm = new FakeLlmProvider(Narration, BuildValidPlanJson());
        var resumeTts = new FakeTtsProvider { WriteOutputFile = true };
        var scriptService = new ScriptGenerationService(resumeLlm, _repository, TimeProvider.System);
        await scriptService.GenerateScriptAsync(resumeJob.Id, CancellationToken.None);
        await new ScenePlanningService(resumeLlm, _repository, _planStore, new InMemoryAssetRepository(), TimeProvider.System)
            .GeneratePlanAsync(resumeJob.Id, CancellationToken.None);
        await new NarrationService(resumeTts, _repository, _planStore, _locator, TimeProvider.System)
            .GenerateNarrationAsync(resumeJob.Id, CancellationToken.None);

        var renderer = new FakeRenderer();
        var untouchedLlm = new FakeLlmProvider();
        var untouchedTts = new FakeTtsProvider();
        await CreatePipeline(untouchedLlm, untouchedTts, renderer).ProcessAsync(Generate(resumeJob), CancellationToken.None);

        Assert.Equal(VideoJobStatus.Completed, (await GetAsync(resumeJob)).Status);
        Assert.Empty(untouchedLlm.ReceivedPrompts);
        Assert.Empty(untouchedTts.Requests);
        Assert.Single(renderer.Requests);
    }

    [Fact]
    public async Task ProcessAsync_EngineMissingInTheBackground_FailsTheJobWithTheSetupHint()
    {
        var job = await AddJobAsync();
        var llm = new FakeLlmProvider(Narration, BuildValidPlanJson());

        await CreatePipeline(llm, new FakeTtsProvider { IsReady = false }, new FakeRenderer())
            .ProcessAsync(Generate(job), CancellationToken.None);

        var failed = await GetAsync(job);
        Assert.Equal(VideoJobStatus.Failed, failed.Status);
        Assert.Contains("Run install-fake.ps1", failed.ErrorMessage);
    }

    [Fact]
    public async Task ProcessAsync_ScriptGenerationFails_StopsWithoutRunningLaterStages()
    {
        var job = await AddJobAsync();
        var llm = new FakeLlmProvider("too short", "still short", "short again");
        var tts = new FakeTtsProvider();
        var renderer = new FakeRenderer();

        await CreatePipeline(llm, tts, renderer).ProcessAsync(Generate(job), CancellationToken.None);

        Assert.Equal(VideoJobStatus.Failed, (await GetAsync(job)).Status);
        Assert.Empty(tts.Requests);
        Assert.Empty(renderer.Requests);
    }

    [Fact]
    public async Task ProcessAsync_UnexpectedException_FailsTheJobInsteadOfThrowing()
    {
        var job = await AddJobAsync();
        var llm = new FakeLlmProvider(new NullReferenceException("boom"));

        await CreatePipeline(llm, new FakeTtsProvider(), new FakeRenderer()).ProcessAsync(Generate(job), CancellationToken.None);

        var failed = await GetAsync(job);
        Assert.Equal(VideoJobStatus.Failed, failed.Status);
        Assert.Equal("Unexpected error: boom", failed.ErrorMessage);
    }

    [Fact]
    public async Task ProcessAsync_CompletedJob_GenerateDoesNothing_RenderReRenders()
    {
        var job = await AddJobAsync();
        var renderer = new FakeRenderer();
        var pipeline = CreatePipeline(
            new FakeLlmProvider(Narration, BuildValidPlanJson()), new FakeTtsProvider { WriteOutputFile = true }, renderer);
        await pipeline.ProcessAsync(Generate(job), CancellationToken.None);

        await pipeline.ProcessAsync(Generate(job), CancellationToken.None);
        Assert.Single(renderer.Requests);

        await pipeline.ProcessAsync(new VideoJobWorkItem(job.Id, VideoJobWorkKind.Render), CancellationToken.None);
        Assert.Equal(2, renderer.Requests.Count);
        Assert.Equal(VideoJobStatus.Completed, (await GetAsync(job)).Status);
    }

    [Fact]
    public async Task ProcessAsync_Renarrate_RegeneratesTheVoiceOverThenRenders_AndClearsTheOutdatedMark()
    {
        var job = await AddJobAsync();
        var tts = new FakeTtsProvider { WriteOutputFile = true };
        var renderer = new FakeRenderer();
        var pipeline = CreatePipeline(new FakeLlmProvider(Narration, BuildValidPlanJson()), tts, renderer);
        await pipeline.ProcessAsync(Generate(job), CancellationToken.None);
        var plan = (await _planStore.LoadAsync(job.Id, CancellationToken.None))!;
        await _planStore.SaveAsync(job.Id, plan with { NarrationOutdated = true }, CancellationToken.None);

        await pipeline.ProcessAsync(new VideoJobWorkItem(job.Id, VideoJobWorkKind.Renarrate), CancellationToken.None);

        Assert.Equal(2, tts.Requests.Count);
        Assert.Equal(2, renderer.Requests.Count);
        Assert.Equal(VideoJobStatus.Completed, (await GetAsync(job)).Status);
        Assert.False((await _planStore.LoadAsync(job.Id, CancellationToken.None))!.NarrationOutdated);
    }

    [Fact]
    public async Task ProcessAsync_ShutdownCancellation_PropagatesSoTheWorkerCanStop()
    {
        var job = await AddJobAsync();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var llm = new FakeLlmProvider(new OperationCanceledException(cts.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreatePipeline(llm, new FakeTtsProvider(), new FakeRenderer()).ProcessAsync(Generate(job), cts.Token));
    }

    private VideoGenerationPipeline CreatePipeline(FakeLlmProvider llm, FakeTtsProvider tts, FakeRenderer renderer) =>
        new(
            _repository,
            _planStore,
            new ScriptGenerationService(llm, _repository, TimeProvider.System),
            new ScenePlanningService(llm, _repository, _planStore, new InMemoryAssetRepository(), TimeProvider.System),
            new NarrationService(tts, _repository, _planStore, _locator, TimeProvider.System),
            new RenderService(renderer, _repository, _locator, TimeProvider.System, _planStore),
            TimeProvider.System,
            NullLogger<VideoGenerationPipeline>.Instance);

    private static VideoJobWorkItem Generate(VideoJob job) => new(job.Id, VideoJobWorkKind.Generate);

    private async Task<VideoJob> AddJobAsync()
    {
        var job = VideoJob.Create("Why Your Pull-Ups Aren't Increasing", null, Now);
        await _repository.AddAsync(job, CancellationToken.None);
        return job;
    }

    private async Task<VideoJob> GetAsync(VideoJob job) =>
        (await _repository.GetByIdAsync(job.Id, CancellationToken.None))!;

    private static string BuildValidPlanJson() => VisualPlanJson.For(Narration);
}
