using System.Text.Json;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Application.Tests;

public class ScenePlanningServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string Narration = TestNarrations.PullUps;

    [Fact]
    public async Task GeneratePlanAsync_ValidFirstAttempt_SavesPlanAndSetsOutputDirectory()
    {
        var repository = new InMemoryVideoJobRepository();
        var job = VideoJob.Create("Why Your Pull-Ups Aren't Increasing", null, Now);
        job.MarkPlanning(Now);
        job.AttachGeneratedScript(Narration, Now);
        await repository.AddAsync(job, CancellationToken.None);

        var llm = new FakeLlmProvider(BuildValidPlanJson());
        var planStore = new InMemoryVideoPlanStore();
        var service = new ScenePlanningService(llm, repository, planStore, new InMemoryAssetRepository(), TimeProvider.System);

        var plan = await service.GeneratePlanAsync(job.Id, CancellationToken.None);

        Assert.Equal(7, plan.Scenes.Count);
        Assert.Equal(Narration, plan.NarrationText);

        var persistedJob = await repository.GetByIdAsync(job.Id, CancellationToken.None);
        Assert.Equal($"output/{job.Id}", persistedJob!.OutputDirectory);

        var savedPlan = await planStore.LoadAsync(job.Id, CancellationToken.None);
        Assert.NotNull(savedPlan);
    }

    [Fact]
    public async Task GeneratePlanAsync_FirstAttemptInvalidJson_RetriesAndSucceeds()
    {
        var repository = new InMemoryVideoJobRepository();
        var job = VideoJob.Create("Running basics", null, Now);
        job.MarkPlanning(Now);
        job.AttachGeneratedScript(Narration, Now);
        await repository.AddAsync(job, CancellationToken.None);

        var llm = new FakeLlmProvider("not json at all", BuildValidPlanJson());
        var service = new ScenePlanningService(llm, repository, new InMemoryVideoPlanStore(), new InMemoryAssetRepository(), TimeProvider.System);

        var plan = await service.GeneratePlanAsync(job.Id, CancellationToken.None);

        Assert.Equal(7, plan.Scenes.Count);
        Assert.Equal(2, llm.ReceivedPrompts.Count);
    }

    [Fact]
    public async Task GeneratePlanAsync_AllAttemptsInvalid_MarksJobFailedAndThrows()
    {
        var repository = new InMemoryVideoJobRepository();
        var job = VideoJob.Create("Running basics", null, Now);
        job.MarkPlanning(Now);
        job.AttachGeneratedScript(Narration, Now);
        await repository.AddAsync(job, CancellationToken.None);

        var llm = new FakeLlmProvider("not json", "still not json", "nope", "nope again", "still nope");
        var service = new ScenePlanningService(llm, repository, new InMemoryVideoPlanStore(), new InMemoryAssetRepository(), TimeProvider.System);

        await Assert.ThrowsAsync<VideoPlanGenerationException>(
            () => service.GeneratePlanAsync(job.Id, CancellationToken.None));

        var persisted = await repository.GetByIdAsync(job.Id, CancellationToken.None);
        Assert.Equal(VideoJobStatus.Failed, persisted!.Status);
    }

    [Fact]
    public async Task GeneratePlanAsync_OllamaUnavailable_MarksJobFailedAndRethrows()
    {
        var repository = new InMemoryVideoJobRepository();
        var job = VideoJob.Create("Running basics", null, Now);
        job.MarkPlanning(Now);
        job.AttachGeneratedScript(Narration, Now);
        await repository.AddAsync(job, CancellationToken.None);

        var llm = new FakeLlmProvider(new LlmUnavailableException("Could not reach Ollama."));
        var service = new ScenePlanningService(llm, repository, new InMemoryVideoPlanStore(), new InMemoryAssetRepository(), TimeProvider.System);

        await Assert.ThrowsAsync<LlmUnavailableException>(
            () => service.GeneratePlanAsync(job.Id, CancellationToken.None));

        var persisted = await repository.GetByIdAsync(job.Id, CancellationToken.None);
        Assert.Equal(VideoJobStatus.Failed, persisted!.Status);
    }

    [Fact]
    public async Task GeneratePlanAsync_JobHasNoScriptYet_Throws()
    {
        var repository = new InMemoryVideoJobRepository();
        var job = VideoJob.Create("Running basics", null, Now);
        await repository.AddAsync(job, CancellationToken.None);

        var service = new ScenePlanningService(
            new FakeLlmProvider(), repository, new InMemoryVideoPlanStore(), new InMemoryAssetRepository(), TimeProvider.System);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GeneratePlanAsync(job.Id, CancellationToken.None));
    }

    [Fact]
    public async Task GeneratePlanAsync_SceneReferencesKnownAsset_Succeeds()
    {
        var repository = new InMemoryVideoJobRepository();
        var job = VideoJob.Create("Running basics", null, Now);
        job.MarkPlanning(Now);
        job.AttachGeneratedScript(Narration, Now);
        await repository.AddAsync(job, CancellationToken.None);

        var knownAsset = new AssetMetadata(
            "mascot-idle", "assets/mascot/mascot-idle.svg", "mascot", ["mascot"],
            [SceneType.Hook], AssetOrientation.Square, "center");
        var assetRepository = new InMemoryAssetRepository(knownAsset);
        var llm = new FakeLlmProvider(BuildValidPlanJson(firstSceneAssetIds: ["mascot-idle"]));
        var service = new ScenePlanningService(llm, repository, new InMemoryVideoPlanStore(), assetRepository, TimeProvider.System);

        var plan = await service.GeneratePlanAsync(job.Id, CancellationToken.None);

        Assert.Equal(["mascot-idle"], plan.Scenes[0].AssetIds);
        Assert.Contains("mascot-idle", llm.ReceivedPrompts[0]);
    }

    [Fact]
    public async Task GeneratePlanAsync_SceneReferencesUnknownAsset_RetriesAndSucceeds()
    {
        var repository = new InMemoryVideoJobRepository();
        var job = VideoJob.Create("Running basics", null, Now);
        job.MarkPlanning(Now);
        job.AttachGeneratedScript(Narration, Now);
        await repository.AddAsync(job, CancellationToken.None);

        // No assets registered at all, so the model's first attempt (referencing an id that
        // doesn't exist in any real catalog) must be rejected - the planner can only choose
        // known assets, exactly like the roadmap's Day 6 acceptance criterion.
        var llm = new FakeLlmProvider(
            BuildValidPlanJson(firstSceneAssetIds: ["mascot-does-not-exist"]),
            BuildValidPlanJson());
        var service = new ScenePlanningService(
            llm, repository, new InMemoryVideoPlanStore(), new InMemoryAssetRepository(), TimeProvider.System);

        var plan = await service.GeneratePlanAsync(job.Id, CancellationToken.None);

        Assert.Empty(plan.Scenes[0].AssetIds);
        Assert.Equal(2, llm.ReceivedPrompts.Count);
        Assert.Contains("unknown asset id", llm.ReceivedPrompts[1], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GeneratePlanAsync_UnknownJobId_ThrowsKeyNotFound()
    {
        var service = new ScenePlanningService(
            new FakeLlmProvider(), new InMemoryVideoJobRepository(), new InMemoryVideoPlanStore(),
            new InMemoryAssetRepository(), TimeProvider.System);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.GeneratePlanAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task GeneratePlanAsync_NarrationIsSplitInCode_SoItIsReproducedExactly()
    {
        var (repository, job) = await PlannedJobAsync();
        var service = new ScenePlanningService(
            new FakeLlmProvider(BuildValidPlanJson()), repository, new InMemoryVideoPlanStore(), new InMemoryAssetRepository(), TimeProvider.System);

        var plan = await service.GeneratePlanAsync(job.Id, CancellationToken.None);

        Assert.Equal(Narration, string.Join(' ', plan.Scenes.Select(s => s.Narration)));
        Assert.All(plan.Scenes, s => Assert.InRange(s.DurationSeconds, 1, 12));
    }

    [Fact]
    public async Task GeneratePlanAsync_WrongSceneCount_RetriesWithTheExactNumberRequired()
    {
        var (repository, job) = await PlannedJobAsync();
        var llm = new FakeLlmProvider(VisualPlanJson.For(Narration, sceneCountOverride: 5), BuildValidPlanJson());
        var service = new ScenePlanningService(llm, repository, new InMemoryVideoPlanStore(), new InMemoryAssetRepository(), TimeProvider.System);

        await service.GeneratePlanAsync(job.Id, CancellationToken.None);

        Assert.Equal(2, llm.ReceivedPrompts.Count);
        Assert.Contains("You returned 5 scenes; return exactly 7", llm.ReceivedPrompts[1]);
    }

    [Fact]
    public async Task GeneratePlanAsync_ForcesHookFirstAndOutroLast_AndNeutralizesMisplacedOnes()
    {
        var (repository, job) = await PlannedJobAsync();
        // Scene 1: the model forgot the Hook. Scene 3: an Outro in the middle.
        string[] types = ["List", "MythVsFact", "Outro", "Progress", "List", "Comparison", "Recovery"];
        var json = VisualPlanJson.For(Narration, typeFor: i => types[i]);
        var service = new ScenePlanningService(
            new FakeLlmProvider(json), repository, new InMemoryVideoPlanStore(), new InMemoryAssetRepository(), TimeProvider.System);

        var plan = await service.GeneratePlanAsync(job.Id, CancellationToken.None);

        Assert.Equal(SceneType.Hook, plan.Scenes[0].Type);
        Assert.Equal(SceneType.ExerciseDemo, plan.Scenes[2].Type);
        Assert.Equal(SceneType.Outro, plan.Scenes[^1].Type);
    }

    [Fact]
    public async Task GeneratePlanAsync_KeepsEmphasisOnlyWhenItIsSpokenInThatScene()
    {
        var (repository, job) = await PlannedJobAsync();
        var json = VisualPlanJson.For(Narration, emphasisFor: i => i switch
        {
            0 => "pull-ups stuck",       // in scene 1 (the hook sentence on its own)
            1 => "glutes",               // never said
            _ => null,
        });
        var service = new ScenePlanningService(
            new FakeLlmProvider(json), repository, new InMemoryVideoPlanStore(), new InMemoryAssetRepository(), TimeProvider.System);

        var plan = await service.GeneratePlanAsync(job.Id, CancellationToken.None);

        Assert.Equal("pull-ups stuck", plan.Scenes[0].Emphasis);
        Assert.Null(plan.Scenes[1].Emphasis);
    }

    [Fact]
    public async Task GeneratePlanAsync_CounterWithoutANumberInTheText_BecomesPop()
    {
        // Day 29: the number must also be one the scene says, so this narration says it.
        const string narration =
            "Pull-ups stall for a few simple reasons that are easy to fix today. " +
            "Aim for 10 clean reps before you add any extra weight to the bar. " +
            "Start every rep from a dead hang with your arms fully straight below you. " +
            "Pull until your chin clears the bar and squeeze your back at the top. " +
            "Lower yourself slowly instead of dropping down as fast as you can go. " +
            "Rest a full day between hard sessions so your muscles can recover well.";
        var (repository, job) = await PlannedJobAsync(narration);
        var json = VisualPlanJson.For(
            narration,
            animationFor: _ => "counter",
            onScreenTextFor: i => i == 1 ? "10 clean reps" : "Wrong muscle");
        var service = new ScenePlanningService(
            new FakeLlmProvider(json), repository, new InMemoryVideoPlanStore(), new InMemoryAssetRepository(), TimeProvider.System);

        var plan = await service.GeneratePlanAsync(job.Id, CancellationToken.None);

        Assert.Equal(AnimationId.Counter, plan.Scenes[1].Animation);
        Assert.Equal(AnimationId.Pop, plan.Scenes[2].Animation);
    }

    [Fact]
    public async Task GeneratePlanAsync_CarriesTemplateFields_TrimmedAndCleaned()
    {
        var (repository, job) = await PlannedJobAsync();
        var json = VisualPlanJson.For(
            Narration,
            secondaryTextFor: i => i == 1 ? "  Full range wins  " : i == 2 ? "   " : null,
            itemsFor: i => i == 3 ? [" Grip ", "", "Tempo", "Rest", "Sleep"] : []);
        var service = new ScenePlanningService(
            new FakeLlmProvider(json), repository, new InMemoryVideoPlanStore(), new InMemoryAssetRepository(), TimeProvider.System);

        var plan = await service.GeneratePlanAsync(job.Id, CancellationToken.None);

        Assert.Equal("Full range wins", plan.Scenes[1].SecondaryText);
        Assert.Null(plan.Scenes[2].SecondaryText);
        Assert.Equal(["Grip", "Tempo", "Rest"], plan.Scenes[3].Items);
        Assert.Null(plan.Scenes[0].Items);
    }

    [Theory]
    [InlineData("Fact: engaging core", "engaging core")]
    [InlineData("Right way: engaging core", "engaging core")]
    [InlineData("MYTH - more reps always", "more reps always")]
    [InlineData("Wrong way", null)]
    [InlineData("Common myth", null)]
    [InlineData("Factory reset your form", "Factory reset your form")]
    public async Task GeneratePlanAsync_StripsCardLabelsTheTemplatesAlreadyShow(string written, string? expected)
    {
        var (repository, job) = await PlannedJobAsync();
        var json = VisualPlanJson.For(Narration, secondaryTextFor: i => i == 1 ? written : null);
        var service = new ScenePlanningService(
            new FakeLlmProvider(json), repository, new InMemoryVideoPlanStore(), new InMemoryAssetRepository(), TimeProvider.System);

        var plan = await service.GeneratePlanAsync(job.Id, CancellationToken.None);

        Assert.Equal(expected, plan.Scenes[1].SecondaryText);
    }

    [Fact]
    public async Task GeneratePlanAsync_RepetitivePlan_IsRetriedWithTheVarietyIssues()
    {
        var (repository, job) = await PlannedJobAsync();
        var monotonous = VisualPlanJson.For(Narration, typeFor: i => i == 0 ? "Hook" : i == 6 ? "Outro" : "List");
        var llm = new FakeLlmProvider(monotonous, BuildValidPlanJson());
        var service = new ScenePlanningService(llm, repository, new InMemoryVideoPlanStore(), new InMemoryAssetRepository(), TimeProvider.System);

        var plan = await service.GeneratePlanAsync(job.Id, CancellationToken.None);

        Assert.Equal(2, llm.ReceivedPrompts.Count);
        Assert.Contains("Scene type List is used 5 times", llm.ReceivedPrompts[1]);
        Assert.Null(plan.ReviewWarnings);
    }

    [Fact]
    public async Task GeneratePlanAsync_StillRepetitiveAfterRetries_IsAcceptedWithReviewWarningsNotFailed()
    {
        var (repository, job) = await PlannedJobAsync();
        var monotonous = VisualPlanJson.For(Narration, typeFor: i => i == 0 ? "Hook" : i == 6 ? "Outro" : "List");
        var llm = new FakeLlmProvider(monotonous, monotonous, monotonous);
        var service = new ScenePlanningService(llm, repository, new InMemoryVideoPlanStore(), new InMemoryAssetRepository(), TimeProvider.System);

        var plan = await service.GeneratePlanAsync(job.Id, CancellationToken.None);

        Assert.Equal(3, llm.ReceivedPrompts.Count);
        Assert.Contains(plan.ReviewWarnings!, w => w.Contains("List is used 5 times"));
        Assert.Equal(VideoJobStatus.Planning, (await repository.GetByIdAsync(job.Id, CancellationToken.None))!.Status);
    }

    [Fact]
    public async Task GeneratePlanAsync_TakeawayRepeatingAnEarlierVideo_IsFlagged()
    {
        var (repository, job) = await PlannedJobAsync();
        var planStore = new InMemoryVideoPlanStore();
        await AddEarlierVideoAsync(repository, planStore, completed: true, "Strict reps beat sloppy volume for pull-up progress");
        var llm = new FakeLlmProvider(
            BuildValidPlanJson(),
            VisualPlanJson.For(Narration, takeaway: "Dead hang starts make every rep count"));
        var service = new ScenePlanningService(llm, repository, planStore, new InMemoryAssetRepository(), TimeProvider.System);

        var plan = await service.GeneratePlanAsync(job.Id, CancellationToken.None);

        Assert.Contains("takeaway repeats an earlier video", llm.ReceivedPrompts[1]);
        Assert.Equal("Dead hang starts make every rep count", plan.Takeaway);
    }

    [Fact]
    public async Task GeneratePlanAsync_ADraftThatWasNeverMadeIntoAVideo_IsNotAnEarlierVideo()
    {
        // Day 27: plan-only experiments and abandoned jobs never reach the channel; "repeating"
        // them cost a ~12s re-plan for nothing.
        var (repository, job) = await PlannedJobAsync();
        var planStore = new InMemoryVideoPlanStore();
        await AddEarlierVideoAsync(repository, planStore, completed: false, "Strict reps beat sloppy volume for pull-up progress");
        var llm = new FakeLlmProvider(BuildValidPlanJson());
        var service = new ScenePlanningService(llm, repository, planStore, new InMemoryAssetRepository(), TimeProvider.System);

        await service.GeneratePlanAsync(job.Id, CancellationToken.None);

        Assert.Single(llm.ReceivedPrompts);
    }

    private static async Task AddEarlierVideoAsync(
        InMemoryVideoJobRepository repository, InMemoryVideoPlanStore planStore, bool completed, string takeaway)
    {
        var earlierJob = VideoJob.Create("Pull-up basics", null, Now.AddDays(-1));
        if (completed)
        {
            earlierJob.MarkPlanning(Now);
            earlierJob.MarkNarrating(Now);
            earlierJob.MarkRendering(Now);
            earlierJob.MarkCompleted("final.mp4", Now);
        }

        await repository.AddAsync(earlierJob, CancellationToken.None);
        await planStore.SaveAsync(
            earlierJob.Id,
            new VideoPlan("Old", "Hook", 30, "n", [], null, null, null, Takeaway: takeaway),
            CancellationToken.None);
    }

    [Fact]
    public async Task GeneratePlanAsync_PromptPlaceholdersCopiedBack_AreTreatedAsMissing()
    {
        // Real Day 22 case: a Hook whose on-screen text was literally "max 5 words"; Day 29: a
        // half copy ("max Form") - placeholders are now in <...>, and any "<"/">" means a copy.
        var (repository, job) = await PlannedJobAsync();
        var json = VisualPlanJson.For(Narration, onScreenTextFor: i => i switch
            {
                0 => ScenePlanPromptBuilder.OnScreenTextPlaceholder,
                1 => "Form <caption",
                _ => $"Scene {(char)('A' + i)}",
            })
            .Replace("\"title\":\"Pull-ups\"", "\"title\":\"short video title\"");
        var service = new ScenePlanningService(
            new FakeLlmProvider(json), repository, new InMemoryVideoPlanStore(), new InMemoryAssetRepository(), TimeProvider.System);

        var plan = await service.GeneratePlanAsync(job.Id, CancellationToken.None);

        Assert.Null(plan.Scenes[0].OnScreenText);
        Assert.Null(plan.Scenes[1].OnScreenText);
        Assert.Equal("Scene C", plan.Scenes[2].OnScreenText);
        Assert.Equal("Untitled", plan.Title);
    }

    [Fact]
    public async Task GeneratePlanAsync_AnInventedNumberOnScreen_IsRetried_AndNeverShownEvenIfTheModelKeepsIt()
    {
        // Real Day 29 text: "60 minutes of rest = 1% muscle growth", said nowhere in the narration.
        var (repository, job) = await PlannedJobAsync();
        var json = VisualPlanJson.For(Narration, onScreenTextFor: i => i == 2 ? "60 minutes = 1% growth" : $"Scene {(char)('A' + i)}");
        var llm = new FakeLlmProvider(json, json, json);
        var service = new ScenePlanningService(llm, repository, new InMemoryVideoPlanStore(), new InMemoryAssetRepository(), TimeProvider.System);

        var plan = await service.GeneratePlanAsync(job.Id, CancellationToken.None);

        Assert.Equal(ScenePlanningService.SoftRuleAttempts, llm.ReceivedPrompts.Count);
        Assert.Contains("never says that number", llm.ReceivedPrompts[1]);
        Assert.Null(plan.Scenes[2].OnScreenText);
        Assert.Contains(plan.ReviewWarnings!, w => w.Contains("60, 1%") && w.Contains("removed from the screen"));
    }

    [Fact]
    public async Task GeneratePlanAsync_KeepsAtMostThreeSoundEffects()
    {
        var (repository, job) = await PlannedJobAsync();
        var json = VisualPlanJson.For(Narration, sfxFor: _ => "pop");
        var service = new ScenePlanningService(
            new FakeLlmProvider(json), repository, new InMemoryVideoPlanStore(), new InMemoryAssetRepository(), TimeProvider.System);

        var plan = await service.GeneratePlanAsync(job.Id, CancellationToken.None);

        Assert.Equal(3, plan.Scenes.Count(s => s.Sfx is not null));
        Assert.All(plan.Scenes.Take(3), s => Assert.Equal(SfxId.Pop, s.Sfx));
    }

    [Theory]
    [InlineData("calm", "calm")]
    [InlineData("CALM", "calm")]
    [InlineData("epic-orchestral", "upbeat")]
    [InlineData(null, "upbeat")]
    public async Task GeneratePlanAsync_MusicMoodIsAlwaysAKnownTrack(string? requested, string expected)
    {
        var (repository, job) = await PlannedJobAsync();
        var service = new ScenePlanningService(
            new FakeLlmProvider(VisualPlanJson.For(Narration, musicMood: requested)), repository,
            new InMemoryVideoPlanStore(), new InMemoryAssetRepository(), TimeProvider.System);

        var plan = await service.GeneratePlanAsync(job.Id, CancellationToken.None);

        Assert.Equal(expected, plan.MusicMood);
    }

    [Fact]
    public async Task GeneratePlanAsync_NarrationTooShortForSixScenes_FailsWithoutAskingTheLlm()
    {
        var repository = new InMemoryVideoJobRepository();
        var job = VideoJob.Create("Rest", null, Now);
        job.MarkPlanning(Now);
        job.AttachGeneratedScript("Rest well tonight.", Now);
        await repository.AddAsync(job, CancellationToken.None);
        var llm = new FakeLlmProvider();
        var service = new ScenePlanningService(llm, repository, new InMemoryVideoPlanStore(), new InMemoryAssetRepository(), TimeProvider.System);

        var ex = await Assert.ThrowsAsync<VideoPlanGenerationException>(() => service.GeneratePlanAsync(job.Id, CancellationToken.None));

        Assert.Contains("too short", ex.Message);
        Assert.Empty(llm.ReceivedPrompts);
    }

    private static async Task<(InMemoryVideoJobRepository Repository, VideoJob Job)> PlannedJobAsync(string? narration = null)
    {
        var repository = new InMemoryVideoJobRepository();
        var job = VideoJob.Create("Why Your Pull-Ups Aren't Increasing", null, Now);
        job.MarkPlanning(Now);
        job.AttachGeneratedScript(narration ?? Narration, Now);
        await repository.AddAsync(job, CancellationToken.None);
        return (repository, job);
    }

    private static string BuildValidPlanJson(IReadOnlyList<string>? firstSceneAssetIds = null) =>
        VisualPlanJson.For(Narration, firstSceneAssetIds);
}
