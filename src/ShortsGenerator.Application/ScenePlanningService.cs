using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Application;

public sealed partial class ScenePlanningService
{
    private const int MaxAttempts = 5;
    private const int MaxSoundEffects = 3;
    public const int SoftRuleAttempts = 3;
    private const int EarlierPlansToCompare = 20;
    // 3-word phrases: takeaways are one short sentence, so 5-word shingles (used for whole
    // scripts) would almost never match even for the same lesson reworded slightly.
    private const int TakeawayShingleSize = 3;
    private const double MaxTakeawayOverlap = 0.6;

    private async Task<IReadOnlyList<(string Topic, VideoPlan Plan)>> LoadEarlierPlansAsync(
        Guid jobId, CancellationToken cancellationToken)
    {
        // Only videos that were actually made (Day 27): variety matters between what ends up on
        // the channel. Drafts - plan-only experiments, abandoned or failed jobs - used to count
        // too, and measurably made planning slower as they piled up (1.5 -> 2.0 attempts per
        // plan, ~12s each) by "repeating" plans nobody will ever see.
        var earlier = new List<(string, VideoPlan)>();
        var recent = await _repository.ListRecentAsync(EarlierPlansToCompare * 5, cancellationToken);
        foreach (var job in recent.Where(j => j.Status is VideoJobStatus.Completed).Take(EarlierPlansToCompare))
        {
            if (job.Id == jobId)
            {
                continue;
            }

            var plan = await _planStore.LoadAsync(job.Id, cancellationToken);
            if (plan is not null)
            {
                earlier.Add((job.Topic, plan));
            }
        }

        return earlier;
    }

    // CLAUDE.md section 4: each Short needs a distinct, meaningful lesson - two videos that
    // teach the same thing in different words are still repetition.
    // A counter with its number removed has nothing to count: same rule as ResolveAnimation.
    private static Scene StripUnsourcedNumbers(Scene scene)
    {
        var stripped = OnScreenNumbers.Strip(scene);
        return stripped.Animation is AnimationId.Counter && !(stripped.OnScreenText ?? "").Any(char.IsDigit)
            ? stripped with { Animation = AnimationId.Pop }
            : stripped;
    }

    private const string UnsourcedNumberMarker = "but its narration never says that number";

    // Day 29 (OnScreenNumbers): a number on screen reads as a fact, so it must be one the scene says.
    private static IEnumerable<string> CheckOnScreenNumbers(VideoPlan plan) =>
        plan.Scenes
            .Select(s => (s.Order, Numbers: OnScreenNumbers.Unsourced(s)))
            .Where(s => s.Numbers.Count > 0)
            .Select(s => $"Scene {s.Order} shows {string.Join(", ", s.Numbers)} on screen, {UnsourcedNumberMarker}; " +
                         "only show numbers this scene's narration says (or none).");

    private static IEnumerable<string> CheckTakeaway(string? takeaway, IReadOnlyList<(string Topic, string Takeaway)> earlier)
    {
        if (string.IsNullOrWhiteSpace(takeaway))
        {
            yield return "\"takeaway\" is empty: state the one lesson viewers should remember.";
            yield break;
        }

        foreach (var (topic, earlierTakeaway) in earlier)
        {
            var normalizedEqual = string.Equals(Normalize(takeaway), Normalize(earlierTakeaway), StringComparison.Ordinal);
            if (normalizedEqual || TextSimilarity.Overlap(takeaway, earlierTakeaway, TakeawayShingleSize) >= MaxTakeawayOverlap)
            {
                yield return $"The takeaway repeats an earlier video (\"{topic}\": \"{earlierTakeaway}\"); give this video its own lesson.";
                yield break;
            }
        }

        static string Normalize(string text) => new(text.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    }

    // Kokoro speaks ~3.5 words/s (measured on Day 12); 3 keeps the pre-narration estimate on
    // the generous side. The real durations replace these once narration exists.
    private const double EstimatedWordsPerSecond = 3.0;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ILlmProvider _llmProvider;
    private readonly IVideoJobRepository _repository;
    private readonly IVideoPlanStore _planStore;
    private readonly IAssetRepository _assetRepository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ScenePlanningService> _logger;

    public ScenePlanningService(
        ILlmProvider llmProvider,
        IVideoJobRepository repository,
        IVideoPlanStore planStore,
        IAssetRepository assetRepository,
        TimeProvider timeProvider,
        ILogger<ScenePlanningService>? logger = null)
    {
        _llmProvider = llmProvider;
        _repository = repository;
        _planStore = planStore;
        _assetRepository = assetRepository;
        _timeProvider = timeProvider;
        _logger = logger ?? NullLogger<ScenePlanningService>.Instance;
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
            var plan = await GenerateValidatedPlanAsync(job.Id, job.Topic, job.GeneratedScript, cancellationToken);
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
        Guid jobId, string topic, string narrationText, CancellationToken cancellationToken)
    {
        var segments = NarrationSegmenter.Split(narrationText, VideoPlanValidator.MinScenes, VideoPlanValidator.MaxScenes);
        if (segments.Count < VideoPlanValidator.MinScenes)
        {
            throw new VideoPlanGenerationException(
                $"The narration is too short to split into at least {VideoPlanValidator.MinScenes} scenes " +
                $"(got {segments.Count}). Use a longer script.");
        }

        var knownAssets = (await _assetRepository.GetAllAsync(cancellationToken))
            .Where(a => !a.Tags.Contains(ArtSubject.WrongFormTag))
            .ToList();
        var knownAssetIds = knownAssets.Select(a => a.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var schema = VisualPlanSchema.Build(segments.Count, knownAssets.Select(a => a.Id));
        var earlierPlans = await LoadEarlierPlansAsync(jobId, cancellationToken);
        var earlierTakeaways = earlierPlans
            .Where(e => !string.IsNullOrWhiteSpace(e.Plan.Takeaway))
            .Select(e => (e.Topic, e.Plan.Takeaway!))
            .ToList();
        // Stable per job: the prompt's template guide is listed in a different order for
        // different jobs, so the model's habit of following list order doesn't give every
        // video the same skeleton (see PlanVariationRules.CheckAgainstEarlier).
        var variationSeed = jobId.GetHashCode() & int.MaxValue;
        IReadOnlyList<string> failureReasons = [];
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var prompt = ScenePlanPromptBuilder.Build(topic, segments, knownAssets, failureReasons, variationSeed);
            var raw = await _llmProvider.GenerateJsonAsync(prompt, schema, cancellationToken);

            var (plan, reasons) = TryBuildPlan(PlanJsonExtractor.Extract(raw), segments, narrationText, knownAssets, knownAssetIds, topic);
            if (plan is not null)
            {
                // Soft rules (variety, unique takeaway): worth a retry while attempts are cheap,
                // never worth failing the video for. From the SoftRuleAttempts-th attempt on, a
                // plan that only breaks soft rules is accepted and the issues go to the human
                // reviewer (review checklist) instead.
                var softIssues = PlanVariationRules.Check(plan)
                    .Concat(PlanVariationRules.CheckAgainstEarlier(plan, earlierPlans))
                    .Concat(CheckTakeaway(plan.Takeaway, earlierTakeaways))
                    .Concat(CheckOnScreenNumbers(plan))
                    .ToList();
                if (softIssues.Count == 0 || attempt >= SoftRuleAttempts)
                {
                    // An invented number never reaches the screen, even when the model kept
                    // writing one: those texts are removed (the captions still carry the scene).
                    plan = plan with { Scenes = plan.Scenes.Select(StripUnsourcedNumbers).ToList() };
                    softIssues = softIssues
                        .Select(issue => issue.Contains(UnsourcedNumberMarker, StringComparison.Ordinal)
                            ? issue.Replace("; only show numbers this scene's narration says (or none).", " - that text was removed from the screen.")
                            : issue)
                        .ToList();
                    _logger.LogInformation(
                        "Job {JobId}: scene plan accepted on attempt {Attempt} ({SceneCount} scenes, {WarningCount} review warnings)",
                        jobId, attempt, segments.Count, softIssues.Count);
                    return softIssues.Count == 0 ? plan : plan with { ReviewWarnings = softIssues };
                }

                reasons = softIssues;
            }

            failureReasons = reasons;
            // Visible per attempt, not just the last one: on Day 15 the first real failure
            // only surfaced its 5th attempt's reason, which made diagnosis guesswork.
            _logger.LogWarning(
                "Job {JobId}: scene plan attempt {Attempt}/{MaxAttempts} rejected: {Reasons}",
                jobId, attempt, MaxAttempts, string.Join(" | ", reasons));
        }

        throw new VideoPlanGenerationException(
            $"Could not produce a valid scene plan for '{topic}' after {MaxAttempts} attempts. " +
            $"Last issues: {string.Join("; ", failureReasons)}");
    }

    private static (VideoPlan? Plan, IReadOnlyList<string> Reasons) TryBuildPlan(
        string json,
        IReadOnlyList<string> segments,
        string narrationText,
        IReadOnlyList<AssetMetadata> knownAssets,
        IReadOnlySet<string> knownAssetIds,
        string topic)
    {
        GeneratedVisualPlan? generated;
        try
        {
            generated = JsonSerializer.Deserialize<GeneratedVisualPlan>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            return (null, [$"Your output was not valid JSON matching the requested shape: {ex.Message}"]);
        }

        if (generated?.Scenes is null)
        {
            return (null, ["Your output had no \"scenes\" array."]);
        }

        if (generated.Scenes.Count != segments.Count)
        {
            return (null, [
                $"You returned {generated.Scenes.Count} scenes; return exactly {segments.Count}, one per numbered narration piece.",
            ]);
        }

        var scenes = new List<Scene>(segments.Count);
        var sfxUsed = 0;
        for (var i = 0; i < segments.Count; i++)
        {
            var visual = generated.Scenes[i];
            // Restraint (CLAUDE.md section 13: effects must not be overwhelming): only the
            // first MaxSoundEffects requested effects are kept, whatever the model asked for.
            var sfx = visual.Sfx is not null && sfxUsed < MaxSoundEffects ? visual.Sfx : null;
            if (sfx is not null)
            {
                sfxUsed++;
            }

            var type = ResolveType(visual.Type, i, segments.Count);
            var onScreenText = CleanCardText(visual.OnScreenText);
            scenes.Add(new Scene(
                Id: $"scene-{i + 1}",
                Order: i + 1,
                Type: type,
                DurationSeconds: EstimateSeconds(segments[i]),
                Narration: segments[i],
                OnScreenText: onScreenText,
                AssetIds: AssetMatcher.Improve(visual.AssetIds ?? [], type, $"{onScreenText} {segments[i]}", knownAssets, topic),
                Animation: ResolveAnimation(visual.Animation, visual.OnScreenText),
                Sfx: sfx,
                Background: null,
                Emphasis: ResolveEmphasis(visual.Emphasis, segments[i]),
                SecondaryText: CleanCardText(visual.SecondaryText),
                Items: CleanItems(visual.Items)));
        }

        // A copied placeholder counts as missing (Day 22), so it gets the same fallback.
        static string? Real(string? text) =>
            string.IsNullOrWhiteSpace(text) || ScenePlanPromptBuilder.IsPlaceholder(text) ? null : text.Trim();

        var plan = new VideoPlan(
            Real(generated.Title) ?? "Untitled",
            Real(generated.Hook) ?? segments[0],
            scenes.Sum(s => s.DurationSeconds),
            narrationText,
            // Repetition repaired in code rather than by another ~13s attempt (Day 29).
            AssetMatcher.Spread(scenes, knownAssets, topic),
            Style: null,
            CaptionStyle: null,
            MusicMood: MusicMood.Normalize(generated.MusicMood),
            // A missing takeaway is then flagged by the soft rules and asked for again.
            Takeaway: Real(generated.Takeaway));

        var validation = VideoPlanValidator.Validate(plan, narrationText, knownAssetIds);
        return validation.IsValid ? (plan, []) : (null, validation.Reasons);
    }

    // Hook-first / Outro-last is structure, not creativity: enforced in code rather than
    // spending a retry when the model mislabels them. A Hook/Outro the model put in the
    // middle (or no type at all) becomes a neutral ExerciseDemo.
    private static SceneType ResolveType(SceneType? requested, int index, int count) =>
        index == 0 ? SceneType.Hook
        : index == count - 1 ? SceneType.Outro
        : requested is null or SceneType.Hook or SceneType.Outro ? SceneType.ExerciseDemo
        : requested.Value;

    // "counter" counts up the first number in the on-screen text; with no number it does
    // nothing, which the Day 15 video showed ("Wrong Muscle" + counter). "pop" is the
    // closest entrance with visible motion.
    private static AnimationId ResolveAnimation(AnimationId? requested, string? onScreenText) =>
        requested switch
        {
            null => AnimationId.Fade,
            AnimationId.Counter when onScreenText is null || !onScreenText.Any(char.IsAsciiDigit) => AnimationId.Pop,
            _ => requested.Value,
        };

    // Emphasis must be words that are actually spoken in this scene, otherwise the caption
    // highlight has nothing to point at. Cosmetic, so dropped rather than retried.
    private static string? ResolveEmphasis(string? emphasis, string narration) =>
        !string.IsNullOrWhiteSpace(emphasis) && narration.Contains(emphasis.Trim(), StringComparison.OrdinalIgnoreCase)
            ? emphasis.Trim()
            : null;

    // The templates already label their cards (MYTH / FACT, WRONG / RIGHT). Found in a real
    // Day 18 run: the model also wrote the label into the text ("Fact: engaging core"),
    // which would render as a doubled label. The prefix is dropped; text that was *only* a
    // label (e.g. "Wrong way") becomes null so the template falls back to a single card.
    private static string? CleanCardText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || ScenePlanPromptBuilder.IsPlaceholder(text))
        {
            return null;
        }

        var cleaned = CardLabelPrefix().Replace(text.Trim(), string.Empty).Trim();
        return cleaned.Length == 0 || CardLabelOnly().IsMatch(cleaned) ? null : cleaned;
    }

    [System.Text.RegularExpressions.GeneratedRegex(
        @"^(myth|fact|truth|wrong(\s+way)?|right(\s+way)?|correct(\s+way)?)\s*[:\-–—]\s*",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex CardLabelPrefix();

    [System.Text.RegularExpressions.GeneratedRegex(
        @"^(common\s+)?(myth|fact|truth|wrong(\s+way)?|right(\s+way)?|correct(\s+way)?)$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex CardLabelOnly();

    // Bullets are on-screen text: trimmed, empties dropped, at most 3 (the schema already
    // caps the count; this also covers hand-edited plans). Null when there's nothing left,
    // so templates can simply check for presence.
    private static IReadOnlyList<string>? CleanItems(IReadOnlyList<string>? items)
    {
        var cleaned = (items ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Take(3)
            .ToList();
        return cleaned.Count == 0 ? null : cleaned;
    }

    private static double EstimateSeconds(string narration)
    {
        var words = narration.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        return Math.Clamp(
            Math.Round(words / EstimatedWordsPerSecond, MidpointRounding.AwayFromZero),
            VideoPlanValidator.MinSceneDurationSeconds,
            VideoPlanValidator.MaxSceneDurationSeconds);
    }

    // The shape the LLM must produce: visual choices only, no narration and no timing -
    // both are already known exactly and are filled in by code.
    private sealed record GeneratedVisualPlan(
        string? Title, string? Hook, string? MusicMood, IReadOnlyList<GeneratedSceneVisual>? Scenes, string? Takeaway = null);

    private sealed record GeneratedSceneVisual(
        SceneType? Type,
        string? OnScreenText,
        IReadOnlyList<string>? AssetIds,
        AnimationId? Animation,
        SfxId? Sfx,
        string? Emphasis,
        string? SecondaryText = null,
        IReadOnlyList<string>? Items = null);
}
