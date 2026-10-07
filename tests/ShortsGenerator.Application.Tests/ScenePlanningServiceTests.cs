using System.Text.Json;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Application.Tests;

public class ScenePlanningServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string Narration =
        "Pull ups are a great back exercise. Keep your core tight throughout the movement. " +
        "Pull your chest towards the bar, not just your chin. Lower yourself under control " +
        "every single rep. Add weight once you can do ten clean reps. Track your progress " +
        "every single week.";

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
        var service = new ScenePlanningService(llm, repository, planStore, TimeProvider.System);

        var plan = await service.GeneratePlanAsync(job.Id, CancellationToken.None);

        Assert.Equal(6, plan.Scenes.Count);
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
        var service = new ScenePlanningService(llm, repository, new InMemoryVideoPlanStore(), TimeProvider.System);

        var plan = await service.GeneratePlanAsync(job.Id, CancellationToken.None);

        Assert.Equal(6, plan.Scenes.Count);
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

        var llm = new FakeLlmProvider("not json", "still not json", "nope");
        var service = new ScenePlanningService(llm, repository, new InMemoryVideoPlanStore(), TimeProvider.System);

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
        var service = new ScenePlanningService(llm, repository, new InMemoryVideoPlanStore(), TimeProvider.System);

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
            new FakeLlmProvider(), repository, new InMemoryVideoPlanStore(), TimeProvider.System);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GeneratePlanAsync(job.Id, CancellationToken.None));
    }

    [Fact]
    public async Task GeneratePlanAsync_UnknownJobId_ThrowsKeyNotFound()
    {
        var service = new ScenePlanningService(
            new FakeLlmProvider(), new InMemoryVideoJobRepository(), new InMemoryVideoPlanStore(), TimeProvider.System);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.GeneratePlanAsync(Guid.NewGuid(), CancellationToken.None));
    }

    private static string BuildValidPlanJson()
    {
        var sentences = Narration.Split(". ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.TrimEnd('.'))
            .ToList();

        var scenes = new List<Scene>();
        for (var i = 0; i < sentences.Count; i++)
        {
            var type = i == 0 ? SceneType.Hook : i == sentences.Count - 1 ? SceneType.Outro : SceneType.ExerciseDemo;
            scenes.Add(new Scene(
                Id: $"scene-{i + 1}",
                Order: i + 1,
                Type: type,
                DurationSeconds: 5,
                Narration: sentences[i] + ".",
                OnScreenText: null,
                AssetIds: [],
                Animation: AnimationId.Fade,
                Sfx: null,
                Background: null,
                Emphasis: null));
        }

        return JsonSerializer.Serialize(
            new
            {
                title = "Why Your Pull-Ups Aren't Increasing",
                hook = "Stuck at the same pull-up count for months?",
                targetDurationSeconds = scenes.Sum(s => s.DurationSeconds),
                style = (string?)null,
                captionStyle = (string?)null,
                musicMood = (string?)null,
                scenes,
            },
            JsonOptions);
    }
}
