using ShortsGenerator.Domain;

namespace ShortsGenerator.Application.Tests;

public sealed class ReviewChecklistTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    private readonly TempJobOutputLocator _locator = new();

    public void Dispose() => _locator.Dispose();

    [Fact]
    public void Build_ListsWarningsScenesAndTheHumanChecklist()
    {
        var job = VideoJob.Create("Pull-ups", null, Now);
        var plan = new VideoPlan(
            "Fix Your Pull-Ups", "Stuck?", 31.5, "Full narration here.",
            [
                new Scene("s1", 1, SceneType.Hook, 4, "a", "Plateau", [], AnimationId.PunchZoom, null, null, null),
                new Scene("s2", 2, SceneType.MythVsFact, 4, "b", "More reps", ["mascot-thinking"], AnimationId.Fade, null, null, null,
                    SecondaryText: "Strict reps"),
            ],
            null, null, "upbeat",
            Takeaway: "Strict reps beat volume",
            ReviewWarnings: ["Scene type List is used 3 times; use each type at most 2 times."]);

        var markdown = ReviewChecklist.Build(job, plan, Now);

        Assert.Contains("# Review before publishing: Fix Your Pull-Ups", markdown);
        Assert.Contains("Takeaway: Strict reps beat volume", markdown);
        Assert.Contains("⚠ Scene type List is used 3 times", markdown);
        Assert.Contains("| 2 | MythVsFact | More reps / Strict reps | mascot-thinking |", markdown);
        Assert.Contains("- [ ] Every claim is accurate and cautiously worded", markdown);
        Assert.Contains("V1 never uploads", markdown);
        Assert.Contains("Length: 31.5s", markdown);
        Assert.EndsWith("Full narration here." + Environment.NewLine, markdown);
    }

    [Fact]
    public void Build_WithoutWarnings_SaysSo()
    {
        var job = VideoJob.Create("Pull-ups", null, Now);
        var plan = new VideoPlan("T", "H", 30, "n", [], null, null, null);

        Assert.Contains("No variety or originality warnings.", ReviewChecklist.Build(job, plan, Now));
    }

    [Fact]
    public async Task RenderService_WritesTheChecklistNextToTheVideo()
    {
        var repository = new InMemoryVideoJobRepository();
        var planStore = new InMemoryVideoPlanStore(_locator);
        var job = VideoJob.Create("Pull-ups", null, Now);
        job.MarkPlanning(Now);
        job.AttachGeneratedScript("script", Now);
        job.MarkNarrating(Now);
        await repository.AddAsync(job, CancellationToken.None);
        await planStore.SaveAsync(job.Id, new VideoPlan("My Short", "H", 30, "n", [], null, null, null), CancellationToken.None);
        File.WriteAllBytes(Path.Combine(_locator.GetJobDirectory(job.Id), "narration.wav"), [1]);
        var writer = new TempArtifactWriter(_locator);

        await new RenderService(new FakeRenderer(), repository, _locator, TimeProvider.System, planStore, writer)
            .RenderAsync(job.Id, CancellationToken.None);

        var (fileName, content) = Assert.Single(writer.Written);
        Assert.Equal("review.md", fileName);
        Assert.Contains("My Short", content);
    }
}
