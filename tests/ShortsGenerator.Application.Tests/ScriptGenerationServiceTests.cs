using ShortsGenerator.Domain;

namespace ShortsGenerator.Application.Tests;

public class ScriptGenerationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    private static string ValidNarration(string seed = "word") =>
        string.Join(' ', Enumerable.Repeat(seed, ScriptValidator.MinWords));

    [Fact]
    public async Task GenerateScriptAsync_ValidFirstAttempt_MarksJobNarrating()
    {
        var repository = new InMemoryVideoJobRepository();
        var job = VideoJob.Create("Why Your Pull-Ups Aren't Increasing", null, Now);
        await repository.AddAsync(job, CancellationToken.None);
        var llm = new FakeLlmProvider(ValidNarration());
        var service = new ScriptGenerationService(llm, repository, TimeProvider.System);

        var result = await service.GenerateScriptAsync(job.Id, CancellationToken.None);

        // Script generation only attaches the script; the job stays "Planning" until
        // scene planning (Day 5) and, later, real TTS narration (Day 11) move it further.
        Assert.Equal(VideoJobStatus.Planning, result.Status);
        Assert.Equal(ValidNarration(), result.GeneratedScript);
        Assert.Single(llm.ReceivedPrompts);
    }

    [Fact]
    public async Task GenerateScriptAsync_WithValidSourceScript_UsesItWithoutCallingTheLlm()
    {
        var repository = new InMemoryVideoJobRepository();
        var job = VideoJob.Create("Pull-ups", ValidNarration("rep"), Now);
        await repository.AddAsync(job, CancellationToken.None);
        var llm = new FakeLlmProvider();
        var service = new ScriptGenerationService(llm, repository, TimeProvider.System);

        var result = await service.GenerateScriptAsync(job.Id, CancellationToken.None);

        Assert.Equal(ValidNarration("rep"), result.GeneratedScript);
        Assert.Equal(VideoJobStatus.Planning, result.Status);
        Assert.Empty(llm.ReceivedPrompts);
    }

    [Fact]
    public async Task GenerateScriptAsync_WithInvalidSourceScript_FailsWithoutRewritingIt()
    {
        var repository = new InMemoryVideoJobRepository();
        var job = VideoJob.Create("Pull-ups", "Way too short.", Now);
        await repository.AddAsync(job, CancellationToken.None);
        var llm = new FakeLlmProvider(ValidNarration());
        var service = new ScriptGenerationService(llm, repository, TimeProvider.System);

        var ex = await Assert.ThrowsAsync<ScriptGenerationException>(() =>
            service.GenerateScriptAsync(job.Id, CancellationToken.None));

        Assert.Contains("supplied script", ex.Message);
        Assert.Empty(llm.ReceivedPrompts);
        Assert.Equal(VideoJobStatus.Failed, (await repository.GetByIdAsync(job.Id, CancellationToken.None))!.Status);
    }

    [Fact]
    public async Task GenerateScriptAsync_NearCopyOfAnEarlierVideo_IsRewritten()
    {
        var repository = new InMemoryVideoJobRepository();
        var earlier = VideoJob.Create("Pull-up basics", null, Now.AddDays(-1));
        earlier.MarkPlanning(Now);
        earlier.AttachGeneratedScript(TestNarrations.PullUps, Now);
        await repository.AddAsync(earlier, CancellationToken.None);
        var job = VideoJob.Create("Pull-up plateaus", null, Now);
        await repository.AddAsync(job, CancellationToken.None);

        var fresh = string.Join(' ', Enumerable.Range(1, ScriptValidator.TargetMinWords).Select(i => $"word{i}"));
        var llm = new FakeLlmProvider(TestNarrations.PullUps, fresh);
        var service = new ScriptGenerationService(llm, repository, TimeProvider.System);

        var result = await service.GenerateScriptAsync(job.Id, CancellationToken.None);

        Assert.Equal(fresh, result.GeneratedScript);
        Assert.Contains("Too similar to an earlier video (\"Pull-up basics\"): 100% of its phrases repeat", llm.ReceivedPrompts[1]);
    }

    [Fact]
    public async Task GenerateScriptAsync_FirstAttemptTooShort_RetriesAndSucceeds()
    {
        var repository = new InMemoryVideoJobRepository();
        var job = VideoJob.Create("Running basics", null, Now);
        await repository.AddAsync(job, CancellationToken.None);
        var llm = new FakeLlmProvider("too short", ValidNarration());
        var service = new ScriptGenerationService(llm, repository, TimeProvider.System);

        var result = await service.GenerateScriptAsync(job.Id, CancellationToken.None);

        Assert.Equal(VideoJobStatus.Planning, result.Status);
        Assert.Equal(2, llm.ReceivedPrompts.Count);
        Assert.Contains("Too short", llm.ReceivedPrompts[1]);
    }

    [Fact]
    public async Task GenerateScriptAsync_AllAttemptsInvalid_MarksJobFailedAndThrows()
    {
        var repository = new InMemoryVideoJobRepository();
        var job = VideoJob.Create("Running basics", null, Now);
        await repository.AddAsync(job, CancellationToken.None);
        var llm = new FakeLlmProvider("too short", "still short", "still not enough words");
        var service = new ScriptGenerationService(llm, repository, TimeProvider.System);

        await Assert.ThrowsAsync<ScriptGenerationException>(
            () => service.GenerateScriptAsync(job.Id, CancellationToken.None));

        var persisted = await repository.GetByIdAsync(job.Id, CancellationToken.None);
        Assert.Equal(VideoJobStatus.Failed, persisted!.Status);
        Assert.Contains("after 3 attempts", persisted.ErrorMessage);
    }

    [Fact]
    public async Task GenerateScriptAsync_OllamaUnavailable_MarksJobFailedAndRethrows()
    {
        var repository = new InMemoryVideoJobRepository();
        var job = VideoJob.Create("Running basics", null, Now);
        await repository.AddAsync(job, CancellationToken.None);
        var llm = new FakeLlmProvider(new LlmUnavailableException("Could not reach Ollama."));
        var service = new ScriptGenerationService(llm, repository, TimeProvider.System);

        await Assert.ThrowsAsync<LlmUnavailableException>(
            () => service.GenerateScriptAsync(job.Id, CancellationToken.None));

        var persisted = await repository.GetByIdAsync(job.Id, CancellationToken.None);
        Assert.Equal(VideoJobStatus.Failed, persisted!.Status);
        Assert.Equal("Could not reach Ollama.", persisted.ErrorMessage);
    }

    [Fact]
    public async Task GenerateScriptAsync_UnknownJobId_ThrowsKeyNotFound()
    {
        var repository = new InMemoryVideoJobRepository();
        var llm = new FakeLlmProvider();
        var service = new ScriptGenerationService(llm, repository, TimeProvider.System);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => service.GenerateScriptAsync(Guid.NewGuid(), CancellationToken.None));
    }
}
