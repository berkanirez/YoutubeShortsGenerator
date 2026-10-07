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
