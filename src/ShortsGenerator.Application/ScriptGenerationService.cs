using ShortsGenerator.Domain;

namespace ShortsGenerator.Application;

public sealed class ScriptGenerationService
{
    private const int MaxAttempts = 3;

    private readonly ILlmProvider _llmProvider;
    private readonly IVideoJobRepository _repository;
    private readonly TimeProvider _timeProvider;

    public ScriptGenerationService(ILlmProvider llmProvider, IVideoJobRepository repository, TimeProvider timeProvider)
    {
        _llmProvider = llmProvider;
        _repository = repository;
        _timeProvider = timeProvider;
    }

    public async Task<VideoJob> GenerateScriptAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await _repository.GetByIdAsync(jobId, cancellationToken)
            ?? throw new KeyNotFoundException($"Video job '{jobId}' was not found.");

        job.MarkPlanning(_timeProvider.GetUtcNow());

        try
        {
            var narration = await GenerateValidatedNarrationAsync(job.Topic, cancellationToken);
            job.AttachGeneratedScript(narration, _timeProvider.GetUtcNow());
        }
        catch (Exception ex) when (ex is LlmUnavailableException or ScriptGenerationException)
        {
            job.MarkFailed(ex.Message, _timeProvider.GetUtcNow());
            await _repository.UpdateAsync(job, cancellationToken);
            throw;
        }

        await _repository.UpdateAsync(job, cancellationToken);
        return job;
    }

    private async Task<string> GenerateValidatedNarrationAsync(string topic, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> failureReasons = [];

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var prompt = ScriptPromptBuilder.Build(topic, failureReasons);
            var raw = await _llmProvider.GenerateAsync(prompt, cancellationToken);
            var cleaned = ScriptTextCleaner.Clean(raw);
            var validation = ScriptValidator.Validate(cleaned);

            if (validation.IsValid)
            {
                return cleaned;
            }

            failureReasons = validation.Reasons;
        }

        throw new ScriptGenerationException(
            $"Could not produce a valid narration for '{topic}' after {MaxAttempts} attempts. " +
            $"Last issues: {string.Join("; ", failureReasons)}");
    }
}
