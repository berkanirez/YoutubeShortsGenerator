using ShortsGenerator.Domain;

namespace ShortsGenerator.Application;

/// <summary>
/// A partial update to one scene: a null field means "leave as is"; an empty string (or
/// empty list) clears an optional field.
/// </summary>
public sealed record SceneEdit(
    string? OnScreenText = null,
    string? SecondaryText = null,
    IReadOnlyList<string>? Items = null,
    AnimationId? Animation = null,
    SfxId? Sfx = null,
    bool ClearSfx = false,
    string? Emphasis = null,
    SceneType? Type = null,
    string? Narration = null);

public sealed record PlanSettingsEdit(string? CaptionStyle = null, string? MusicMood = null);

public sealed class PlanEditException : Exception
{
    public PlanEditException(string message)
        : base(message)
    {
    }
}

// Small, safe human corrections to a generated plan - not an editor (CLAUDE.md section 20).
// Every edit goes through the same rules a generated plan had to pass, so a hand-made change
// can't produce a plan the pipeline itself would have rejected.
public sealed class PlanEditService
{
    private readonly IVideoJobRepository _repository;
    private readonly IVideoPlanStore _planStore;
    private readonly IAssetRepository _assets;
    private readonly IVideoJobQueue _queue;

    public PlanEditService(
        IVideoJobRepository repository, IVideoPlanStore planStore, IAssetRepository assets, IVideoJobQueue queue)
    {
        _repository = repository;
        _planStore = planStore;
        _assets = assets;
        _queue = queue;
    }

    public async Task<VideoPlan> ChangeSceneAssetAsync(Guid jobId, string sceneId, string assetId, CancellationToken cancellationToken)
    {
        var (job, plan) = await LoadEditableAsync(jobId, cancellationToken);
        var scene = plan.Scenes.FirstOrDefault(s => s.Id == sceneId)
            ?? throw new KeyNotFoundException($"Scene '{sceneId}' is not in this plan.");

        // Only an id from the catalog - the same "known assets only" rule as for the LLM.
        var asset = await _assets.GetByIdAsync(assetId, cancellationToken)
            ?? throw new PlanEditException($"'{assetId}' is not an asset in the catalog.");

        var edited = plan with
        {
            Scenes = plan.Scenes.Select(s => s.Id == sceneId ? s with { AssetIds = [asset.Id] } : s).ToList(),
        };
        return await SaveValidatedAsync(job, edited, cancellationToken);
    }

    public async Task<VideoPlan> UpdateSceneAsync(Guid jobId, string sceneId, SceneEdit edit, CancellationToken cancellationToken)
    {
        var (job, plan) = await LoadEditableAsync(jobId, cancellationToken);
        var scene = plan.Scenes.FirstOrDefault(s => s.Id == sceneId)
            ?? throw new KeyNotFoundException($"Scene '{sceneId}' is not in this plan.");

        // Hook-first / Outro-last is structure (Day 15): those two can't be retyped, and no
        // other scene can become one.
        if (edit.Type is { } newType && newType != scene.Type &&
            (scene.Type is SceneType.Hook or SceneType.Outro || newType is SceneType.Hook or SceneType.Outro))
        {
            throw new PlanEditException("The first scene stays Hook and the last stays Outro; other scenes can't become either.");
        }

        var narrationChanged = edit.Narration is not null &&
            !string.Equals(edit.Narration.Trim(), scene.Narration?.Trim(), StringComparison.Ordinal);
        var narration = narrationChanged ? edit.Narration!.Trim() : scene.Narration;

        var updated = scene with
        {
            OnScreenText = Optional(edit.OnScreenText, scene.OnScreenText),
            SecondaryText = Optional(edit.SecondaryText, scene.SecondaryText),
            Items = edit.Items is null ? scene.Items : CleanItems(edit.Items),
            Animation = edit.Animation ?? scene.Animation,
            Sfx = edit.ClearSfx ? null : edit.Sfx ?? scene.Sfx,
            Emphasis = Optional(edit.Emphasis, scene.Emphasis),
            Type = edit.Type ?? scene.Type,
            Narration = narration,
        };

        var scenes = plan.Scenes.Select(s => s.Id == sceneId ? updated : s).ToList();
        var edited = plan with { Scenes = scenes };

        if (narrationChanged)
        {
            // The plan's NarrationText is, by definition, its scenes' narration joined
            // (VideoPlanValidator checks exactly that), so it's rebuilt - and the new words go
            // through the same safety filter as a generated script (no medical claims).
            var narrationText = string.Join(' ', scenes.OrderBy(s => s.Order).Select(s => s.Narration).Where(n => !string.IsNullOrWhiteSpace(n)));
            var safety = ScriptValidator.Validate(narrationText);
            var unsafeReasons = safety.Reasons.Where(r => r.Contains("disallowed", StringComparison.OrdinalIgnoreCase)).ToList();
            if (unsafeReasons.Count > 0)
            {
                throw new PlanEditException(string.Join("; ", unsafeReasons));
            }

            edited = edited with { NarrationText = narrationText, NarrationOutdated = true };
        }

        return await SaveValidatedAsync(job, edited, cancellationToken);
    }

    public async Task<VideoPlan> UpdateSettingsAsync(Guid jobId, PlanSettingsEdit edit, CancellationToken cancellationToken)
    {
        var (job, plan) = await LoadEditableAsync(jobId, cancellationToken);

        if (edit.CaptionStyle is not null && !CaptionStyleId.IsKnown(edit.CaptionStyle))
        {
            throw new PlanEditException(
                $"Unknown caption style '{edit.CaptionStyle}' (allowed: {string.Join(", ", CaptionStyleId.All)}).");
        }

        if (edit.MusicMood is not null && !MusicMood.All.Contains(edit.MusicMood, StringComparer.OrdinalIgnoreCase))
        {
            throw new PlanEditException($"Unknown music mood '{edit.MusicMood}' (allowed: {string.Join(", ", MusicMood.All)}).");
        }

        var edited = plan with
        {
            CaptionStyle = edit.CaptionStyle?.ToLowerInvariant() ?? plan.CaptionStyle,
            MusicMood = edit.MusicMood is null ? plan.MusicMood : MusicMood.Normalize(edit.MusicMood),
        };
        return await SaveValidatedAsync(job, edited, cancellationToken);
    }

    // null = keep; "" (after trimming) = clear.
    private static string? Optional(string? edit, string? current) =>
        edit is null ? current : string.IsNullOrWhiteSpace(edit) ? null : edit.Trim();

    private static IReadOnlyList<string>? CleanItems(IReadOnlyList<string> items)
    {
        var cleaned = items.Where(i => !string.IsNullOrWhiteSpace(i)).Select(i => i.Trim()).Take(3).ToList();
        return cleaned.Count == 0 ? null : cleaned;
    }

    // The job must have a plan, and must not be in the middle of being rendered or about to
    // be: the renderer reads plan.json, and an edit landing mid-render would make the video
    // a mix of before and after (or the edit silently lost).
    private async Task<(VideoJob Job, VideoPlan Plan)> LoadEditableAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await _repository.GetByIdAsync(jobId, cancellationToken)
            ?? throw new KeyNotFoundException($"Video job '{jobId}' was not found.");

        // PendingWork: queued *or* running (Day 26) - e.g. a re-narration in progress, whose
        // status is only Narrating.
        if (job.Status is VideoJobStatus.Rendering || job.PendingWork is not null || _queue.IsQueued(jobId))
        {
            throw new InvalidOperationException(
                $"Video job '{jobId}' is being processed right now; edit it once it has finished.");
        }

        if (job.Status is VideoJobStatus.Failed)
        {
            throw new InvalidOperationException($"Video job '{jobId}' failed; there is no usable plan to edit.");
        }

        var plan = await _planStore.LoadAsync(jobId, cancellationToken)
            ?? throw new InvalidOperationException($"Video job '{jobId}' has no scene plan yet.");
        return (job, plan);
    }

    private async Task<VideoPlan> SaveValidatedAsync(VideoJob job, VideoPlan edited, CancellationToken cancellationToken)
    {
        var knownIds = (await _assets.GetAllAsync(cancellationToken)).Select(a => a.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // checkDurations: false - once narration exists the scene lengths come from the real
        // audio (Day 12), and the 20-50s / 1-12s limits were sanity checks for the LLM's guesses.
        var validation = VideoPlanValidator.Validate(edited, edited.NarrationText, knownIds, checkDurations: false);
        if (!validation.IsValid)
        {
            throw new PlanEditException($"The edited plan isn't valid: {string.Join("; ", validation.Reasons)}");
        }

        // Variety warnings are recomputed for the reviewer: a hand edit can introduce them.
        var warnings = PlanVariationRules.Check(edited);
        var saved = edited with { ReviewWarnings = warnings.Count == 0 ? null : warnings };
        // The job's status doesn't change: a Completed job stays Completed, its video is simply
        // outdated (plan.json newer than final.mp4) until it's re-rendered.
        await _planStore.SaveAsync(job.Id, saved, cancellationToken);
        return saved;
    }
}
