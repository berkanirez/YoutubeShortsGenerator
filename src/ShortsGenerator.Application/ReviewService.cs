using System.Text.Json;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Application;

public sealed record ReviewItem(string Text, bool Checked);

/// <param name="ReviewedAt">Set once every item is ticked - and only for the video as it is now.</param>
/// <param name="Stale">The video was re-rendered after it was reviewed: what was checked isn't this video.</param>
public sealed record ReviewStatus(IReadOnlyList<ReviewItem> Items, DateTimeOffset? ReviewedAt, bool Stale)
{
    public bool IsReviewed => ReviewedAt is not null && !Stale;
}

// Day 28: the human sign-off before publishing (CLAUDE.md section 4). The app never uploads;
// this only records, per video, that a person went through the checklist.
public sealed class ReviewService
{
    public const string StateFileName = "review-state.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IVideoJobRepository _repository;
    private readonly IJobArtifactWriter _artifacts;
    private readonly IJobOutputLocator _locator;
    private readonly TimeProvider _timeProvider;
    private readonly OutputIndexService? _index;

    public ReviewService(
        IVideoJobRepository repository, IJobArtifactWriter artifacts, IJobOutputLocator locator, TimeProvider timeProvider,
        OutputIndexService? index = null)
    {
        _repository = repository;
        _artifacts = artifacts;
        _locator = locator;
        _timeProvider = timeProvider;
        _index = index;
    }

    public async Task<ReviewStatus> GetAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var state = await LoadStateAsync(jobId, cancellationToken);
        var items = ReviewChecklist.HumanChecks
            .Select((text, i) => new ReviewItem(text, state is not null && i < state.Checked.Count && state.Checked[i]))
            .ToList();
        return new ReviewStatus(items, state?.ReviewedAt, IsStale(jobId, state?.VideoWrittenAt));
    }

    public async Task<ReviewStatus> SaveAsync(Guid jobId, IReadOnlyList<bool> checkedItems, CancellationToken cancellationToken)
    {
        var job = await _repository.GetByIdAsync(jobId, cancellationToken)
            ?? throw new KeyNotFoundException($"Video job '{jobId}' was not found.");
        if (job.Status is not VideoJobStatus.Completed || job.PendingWork is not null)
        {
            throw new InvalidOperationException($"Video job '{jobId}' has no finished video to review right now.");
        }

        if (checkedItems.Count != ReviewChecklist.HumanChecks.Count)
        {
            throw new ArgumentException(
                $"Expected {ReviewChecklist.HumanChecks.Count} checklist answers, got {checkedItems.Count}.", nameof(checkedItems));
        }

        var all = checkedItems.All(c => c);
        var state = new ReviewState(
            checkedItems.ToList(),
            all ? _timeProvider.GetUtcNow() : null,
            // Pins the review to this exact video file: a later re-render makes it stale.
            File.GetLastWriteTimeUtc(VideoPath(jobId)));
        await _artifacts.WriteTextAsync(jobId, StateFileName, JsonSerializer.Serialize(state, JsonOptions), cancellationToken);
        if (_index is not null)
        {
            await _index.UpdateBestEffortAsync(cancellationToken);
        }

        return await GetAsync(jobId, cancellationToken);
    }

    internal async Task<ReviewState?> LoadStateAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var json = await _artifacts.ReadTextAsync(jobId, StateFileName, cancellationToken);
        if (json is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ReviewState>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;   // a hand-mangled file just means "not reviewed"
        }
    }

    internal bool IsStale(Guid jobId, DateTime? videoWrittenAt) =>
        videoWrittenAt is not null && File.Exists(VideoPath(jobId)) &&
        File.GetLastWriteTimeUtc(VideoPath(jobId)) > videoWrittenAt.Value.AddSeconds(1);

    private string VideoPath(Guid jobId) => Path.Combine(_locator.GetJobDirectory(jobId), RenderService.FinalVideoFileName);
}

internal sealed record ReviewState(List<bool> Checked, DateTimeOffset? ReviewedAt, DateTime? VideoWrittenAt);
