using System.Text.Json;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Application;

public sealed class ScenePlanningService
{
    private const int MaxAttempts = 3;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ILlmProvider _llmProvider;
    private readonly IVideoJobRepository _repository;
    private readonly IVideoPlanStore _planStore;
    private readonly TimeProvider _timeProvider;

    public ScenePlanningService(
        ILlmProvider llmProvider,
        IVideoJobRepository repository,
        IVideoPlanStore planStore,
        TimeProvider timeProvider)
    {
        _llmProvider = llmProvider;
        _repository = repository;
        _planStore = planStore;
        _timeProvider = timeProvider;
    }

    public async Task<VideoPlan> GeneratePlanAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await _repository.GetByIdAsync(jobId, cancellationToken)
            ?? throw new KeyNotFoundException($"Video job '{jobId}' was not found.");

        if (job.Status != VideoJobStatus.Planning || string.IsNullOrWhiteSpace(job.GeneratedScript))
        {
            throw new InvalidOperationException(
                $"Video job '{jobId}' does not have a generated script yet. " +
                "Generate the script (POST /api/videos/{id}/script) before planning scenes.");
        }

        try
        {
            var plan = await GenerateValidatedPlanAsync(job.Topic, job.GeneratedScript, cancellationToken);
            var jobDirectory = await _planStore.SaveAsync(job.Id, plan, cancellationToken);
            job.SetOutputDirectory(jobDirectory, _timeProvider.GetUtcNow());
            await _repository.UpdateAsync(job, cancellationToken);
            return plan;
        }
        catch (Exception ex) when (ex is LlmUnavailableException or VideoPlanGenerationException)
        {
            job.MarkFailed(ex.Message, _timeProvider.GetUtcNow());
            await _repository.UpdateAsync(job, cancellationToken);
            throw;
        }
    }

    private async Task<VideoPlan> GenerateValidatedPlanAsync(
        string topic, string narrationText, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> failureReasons = [];

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var prompt = ScenePlanPromptBuilder.Build(topic, narrationText, failureReasons);
            var raw = await _llmProvider.GenerateAsync(prompt, cancellationToken);
            var json = PlanJsonExtractor.Extract(raw);

            GeneratedScenePlan? generated;
            try
            {
                generated = JsonSerializer.Deserialize<GeneratedScenePlan>(json, JsonOptions);
            }
            catch (JsonException ex)
            {
                failureReasons = [$"Your output was not valid JSON matching the requested shape: {ex.Message}"];
                continue;
            }

            if (generated is null)
            {
                failureReasons = ["Your output parsed to an empty plan."];
                continue;
            }

            var plan = new VideoPlan(
                generated.Title,
                generated.Hook,
                generated.TargetDurationSeconds,
                narrationText,
                generated.Scenes,
                generated.Style,
                generated.CaptionStyle,
                generated.MusicMood);

            var validation = VideoPlanValidator.Validate(plan, narrationText);
            if (validation.IsValid)
            {
                return plan;
            }

            failureReasons = validation.Reasons;
        }

        throw new VideoPlanGenerationException(
            $"Could not produce a valid scene plan for '{topic}' after {MaxAttempts} attempts. " +
            $"Last issues: {string.Join("; ", failureReasons)}");
    }

    // The shape the LLM must produce. Deliberately has no NarrationText field: the model
    // already received the full narration in the prompt and only needs to split it across
    // scenes - asking it to also echo the whole text back at the top level would just be
    // another chance to introduce a transcription error.
    private sealed record GeneratedScenePlan(
        string Title,
        string Hook,
        int TargetDurationSeconds,
        IReadOnlyList<Scene> Scenes,
        string? Style,
        string? CaptionStyle,
        string? MusicMood);
}
