using ShortsGenerator.Domain;

namespace ShortsGenerator.Application.Tests;

// Day 28: the human sign-off before publishing, and output/INDEX.md.
public sealed class ReviewAndIndexTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly InMemoryVideoJobRepository _repository = new();
    private readonly TempJobOutputLocator _locator = new();
    private readonly InMemoryVideoPlanStore _planStore;
    private readonly TempArtifactWriter _artifacts;

    public ReviewAndIndexTests()
    {
        _planStore = new InMemoryVideoPlanStore(_locator);
        _artifacts = new TempArtifactWriter(_locator);
    }

    public void Dispose() => _locator.Dispose();

    private static bool[] All(bool value) => Enumerable.Repeat(value, ReviewChecklist.HumanChecks.Count).ToArray();

    [Fact]
    public async Task NewVideo_HasEveryCheckUnticked_AndIsNotReviewed()
    {
        var job = await CompletedJobAsync("Pull-ups");

        var status = await Reviews().GetAsync(job.Id, CancellationToken.None);

        Assert.Equal(ReviewChecklist.HumanChecks, status.Items.Select(i => i.Text));
        Assert.All(status.Items, i => Assert.False(i.Checked));
        Assert.False(status.IsReviewed);
    }

    [Fact]
    public async Task TickingSomeChecks_IsSaved_ButOnlyAllOfThemMakeItReviewed()
    {
        var job = await CompletedJobAsync("Pull-ups");
        var some = All(true);
        some[2] = false;

        var partial = await Reviews().SaveAsync(job.Id, some, CancellationToken.None);
        var full = await Reviews().SaveAsync(job.Id, All(true), CancellationToken.None);

        Assert.False(partial.IsReviewed);
        Assert.False(partial.Items[2].Checked);
        Assert.True(partial.Items[0].Checked);
        Assert.True(full.IsReviewed);
    }

    [Fact]
    public async Task ARenderAfterTheReview_MakesItStale_BecauseThatVideoWasNeverChecked()
    {
        var job = await CompletedJobAsync("Pull-ups");
        await Reviews().SaveAsync(job.Id, All(true), CancellationToken.None);

        File.SetLastWriteTimeUtc(VideoPath(job), DateTime.UtcNow.AddMinutes(5));   // re-rendered later
        var status = await Reviews().GetAsync(job.Id, CancellationToken.None);

        Assert.True(status.Stale);
        Assert.False(status.IsReviewed);
    }

    [Fact]
    public async Task Review_OnlyForAFinishedVideo_AndWithAnAnswerForEveryCheck()
    {
        var draft = VideoJob.Create("Draft", null, Now);
        await _repository.AddAsync(draft, CancellationToken.None);
        var job = await CompletedJobAsync("Pull-ups");

        await Assert.ThrowsAsync<InvalidOperationException>(() => Reviews().SaveAsync(draft.Id, All(true), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => Reviews().SaveAsync(job.Id, [true, true], CancellationToken.None));
    }

    [Fact]
    public async Task Index_ListsFinishedVideosOnly_WithTitleLengthReviewAndRelativeLinks()
    {
        var reviewed = await CompletedJobAsync("Why Pull-Ups Stall", title: "Fix Your Pull-Ups");
        var notReviewed = await CompletedJobAsync("Rest days");
        var draft = VideoJob.Create("Plan-only experiment", null, Now);
        await _repository.AddAsync(draft, CancellationToken.None);
        await Reviews().SaveAsync(reviewed.Id, All(true), CancellationToken.None);

        var index = _artifacts.Index!;

        Assert.Contains($"| Fix Your Pull-Ups | Why Pull-Ups Stall | 12s | yes | [final.mp4]({reviewed.Id}/final.mp4) |", index);
        Assert.Contains($"| Rest days | 12s | no |", index);
        Assert.DoesNotContain("Plan-only experiment", index);
        Assert.Contains("Nothing is uploaded automatically", index);
    }

    [Fact]
    public async Task Index_TableCellsCantBeBrokenByTitlesWithPipesOrNewlines()
    {
        await CompletedJobAsync("A | B\nC");

        var index = await Index().BuildAsync(CancellationToken.None);

        Assert.Contains("| A / B C |", index);
    }

    private ReviewService Reviews() => new(_repository, _artifacts, _locator, TimeProvider.System, Index());

    private OutputIndexService Index() => new(_repository, _planStore, _artifacts, _locator, TimeProvider.System);

    private string VideoPath(VideoJob job) => Path.Combine(_locator.GetJobDirectory(job.Id), "final.mp4");

    private async Task<VideoJob> CompletedJobAsync(string topic, string title = "T")
    {
        var job = VideoJob.Create(topic, null, Now);
        job.MarkPlanning(Now);
        job.MarkNarrating(Now);
        job.MarkRendering(Now);
        job.MarkCompleted("final.mp4", Now);
        await _repository.AddAsync(job, CancellationToken.None);
        await _planStore.SaveAsync(job.Id, new VideoPlan(title, "H", 12, "n",
            [new Scene("s1", 1, SceneType.Hook, 12, "n", "x", [], AnimationId.Pop, null, null, null)], null, null, null), CancellationToken.None);
        await File.WriteAllBytesAsync(VideoPath(job), [1]);
        return job;
    }
}
