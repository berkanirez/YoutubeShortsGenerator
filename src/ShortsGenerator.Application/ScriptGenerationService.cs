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
            var narration = job.SourceScript is not null
                ? UseSourceScript(job.SourceScript)
                : await GenerateValidatedNarrationAsync(job.Topic, cancellationToken);
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

    // CLAUDE.md section 4: every Short needs a distinct script. A small local model asked
    // about similar topics tends to reuse whole sentences; 5-word phrases shared with an
    // earlier video above this share mean "rewrite it", not "publish a near-copy".
    internal const double MaxPhraseOverlapWithEarlierScripts = 0.25;
    private const int ShingleSize = 5;
    private const int EarlierScriptsToCompare = 30;

    private async Task<IReadOnlyList<(string Topic, string Script)>> LoadEarlierScriptsAsync(CancellationToken cancellationToken)
    {
        var recent = await _repository.ListRecentAsync(EarlierScriptsToCompare, cancellationToken);
        return recent
            .Where(job => !string.IsNullOrWhiteSpace(job.GeneratedScript))
            .Select(job => (job.Topic, job.GeneratedScript!))
            .ToList();
    }

    private static string? FindNearDuplicate(string script, IReadOnlyList<(string Topic, string Script)> earlier)
    {
        var worst = earlier
            .Select(e => (e.Topic, Overlap: TextSimilarity.Overlap(script, e.Script, ShingleSize)))
            .OrderByDescending(e => e.Overlap)
            .FirstOrDefault();

        // Percent written out explicitly: "P0" is culture-dependent ("%25" on this Turkish
        // machine, "25 %" in InvariantCulture), and this text goes into the LLM prompt.
        return worst.Overlap > MaxPhraseOverlapWithEarlierScripts
            ? string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"Too similar to an earlier video (\"{worst.Topic}\"): {worst.Overlap * 100:0}% of its phrases repeat. Write it fresh with different sentences.")
            : null;
    }

    // A user-supplied script is used as written, never sent to the LLM to be "improved":
    // the user chose those words. It still has to pass the same safety/length rules as a
    // generated one - a script the user wrote isn't exempt from the medical-claims check.
    private static string UseSourceScript(string sourceScript)
    {
        var cleaned = ScriptTextCleaner.Clean(sourceScript);
        var validation = ScriptValidator.Validate(cleaned);
        if (!validation.IsValid)
        {
            throw new ScriptGenerationException(
                $"The supplied script can't be used: {string.Join("; ", validation.Reasons)}");
        }

        return cleaned;
    }

    private async Task<string> GenerateValidatedNarrationAsync(string topic, CancellationToken cancellationToken)
    {
        var earlierScripts = await LoadEarlierScriptsAsync(cancellationToken);
        IReadOnlyList<string> failureReasons = [];

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var prompt = ScriptPromptBuilder.Build(topic, failureReasons);
            var raw = await _llmProvider.GenerateAsync(prompt, cancellationToken);
            var cleaned = ScriptTextCleaner.Clean(raw);
            var validation = ScriptValidator.Validate(cleaned);

            if (validation.IsValid)
            {
                var duplicate = FindNearDuplicate(cleaned, earlierScripts);
                if (duplicate is null)
                {
                    return cleaned;
                }

                failureReasons = [duplicate];
                continue;
            }

            failureReasons = validation.Reasons;
        }

        throw new ScriptGenerationException(
            $"Could not produce a valid narration for '{topic}' after {MaxAttempts} attempts. " +
            $"Last issues: {string.Join("; ", failureReasons)}");
    }
}
