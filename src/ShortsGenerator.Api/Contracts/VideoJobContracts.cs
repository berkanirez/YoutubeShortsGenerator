using ShortsGenerator.Application;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Api.Contracts;

/// <param name="AutoGenerate">
/// True (default): queue the whole pipeline for the background worker right away.
/// False: only create the job, for running the stage endpoints by hand.
/// </param>
public sealed record CreateVideoJobRequest(string Topic, string? SourceScript, bool AutoGenerate = true);

public sealed record ChangeSceneAssetRequest(string AssetId);

/// <summary>
/// Scene fields a human may edit (Day 23). Omitted/null = unchanged, "" = clear. Enum-like
/// fields arrive as text and are parsed against the controlled vocabularies.
/// </summary>
public sealed record SceneEditRequest(
    string? OnScreenText = null,
    string? SecondaryText = null,
    IReadOnlyList<string>? Items = null,
    string? Animation = null,
    string? Sfx = null,
    string? Emphasis = null,
    string? Type = null,
    string? Narration = null)
{
    public SceneEdit ToDomain()
    {
        return new SceneEdit(
            OnScreenText,
            SecondaryText,
            Items,
            Parse<AnimationId>(Animation, "animation"),
            string.IsNullOrWhiteSpace(Sfx) ? null : Parse<SfxId>(Sfx, "sfx"),
            ClearSfx: Sfx is not null && string.IsNullOrWhiteSpace(Sfx),
            Emphasis,
            Parse<SceneType>(Type, "type"),
            Narration);

        static T? Parse<T>(string? value, string field) where T : struct, Enum =>
            string.IsNullOrWhiteSpace(value)
                ? null
                : Enum.TryParse<T>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
                    ? parsed
                    : throw new PlanEditException(
                        $"'{value}' is not a valid {field} (allowed: {string.Join(", ", Enum.GetNames<T>())}).");
    }
}

public sealed record PlanSettingsRequest(string? CaptionStyle = null, string? MusicMood = null);

public sealed record VideoJobResponse(
    Guid Id,
    string Topic,
    string? SourceScript,
    string? GeneratedScript,
    string Status,
    bool IsQueued,
    // True only while the background worker is processing this job right now. A job in an
    // in-between status (Planning, Narrating, ...) that is neither queued nor running is
    // paused: driven by hand, or interrupted by a restart - not "in progress".
    bool IsRunning,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? ErrorMessage,
    string? FinalVideoPath,
    // The plan was edited after the video was rendered (Day 22): re-render to see the change.
    bool VideoOutdated = false,
    // Day 28: a person ticked the whole review checklist for this exact render.
    bool IsReviewed = false)
{
    public static VideoJobResponse FromDomain(
        VideoJob job, IVideoJobQueue? queue = null, Guid? runningJobId = null, bool videoOutdated = false, bool isReviewed = false) => new(
        job.Id,
        job.Topic,
        job.SourceScript,
        job.GeneratedScript,
        job.Status.ToString(),
        queue?.IsQueued(job.Id) ?? false,
        runningJobId == job.Id,
        job.CreatedAt,
        job.UpdatedAt,
        job.ErrorMessage,
        job.FinalVideoPath,
        videoOutdated,
        isReviewed);
}

public sealed record ReviewStateRequest(IReadOnlyList<bool> Checked);
