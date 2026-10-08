using Microsoft.Extensions.Options;
using ShortsGenerator.Domain;
using ShortsGenerator.Infrastructure.Storage;

namespace ShortsGenerator.Infrastructure.Tests;

// The .NET half of the plan.json contract test. The renderer's Vitest suite parses
// samples/contract-plan.json with its Zod schema; this test makes sure that file is exactly
// what the backend writes today. Change VideoPlan/Scene or the serializer and this fails with
// the diff; regenerate the fixture with UPDATE_CONTRACT=1 and the renderer tests then tell
// you whether the renderer still accepts it.
public sealed class PlanContractTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "contract-tests-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void CanonicalPlan_PassesTheBackendsOwnValidation()
    {
        var plan = CanonicalPlan();
        var knownIds = plan.Scenes.SelectMany(s => s.AssetIds).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var result = VideoPlanValidator.Validate(plan, plan.NarrationText, knownIds, checkDurations: false);

        Assert.True(result.IsValid, string.Join("; ", result.Reasons));
        Assert.Equal(Enum.GetValues<SceneType>().OrderBy(t => t), plan.Scenes.Select(s => s.Type).Distinct().OrderBy(t => t));
    }

    [Fact]
    public async Task ContractFixture_IsExactlyWhatTheBackendWrites()
    {
        var store = new FileVideoPlanStore(new JobOutputLocator(Options.Create(new OutputStorageOptions { RootDirectory = _root })));
        var directory = await store.SaveAsync(Guid.NewGuid(), CanonicalPlan(), CancellationToken.None);
        var written = Normalize(await File.ReadAllTextAsync(Path.Combine(directory, "plan.json")));
        var fixturePath = Path.Combine(RepositoryRoot(), "samples", "contract-plan.json");

        if (Environment.GetEnvironmentVariable("UPDATE_CONTRACT") == "1" || !File.Exists(fixturePath))
        {
            await File.WriteAllTextAsync(fixturePath, written);
        }

        Assert.Equal(Normalize(await File.ReadAllTextAsync(fixturePath)), written);
    }

    private static string Normalize(string json) => json.Replace("\r\n", "\n").TrimEnd() + "\n";

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ShortsGenerator.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root (ShortsGenerator.slnx) not found.");
    }

    // Every scene type, every optional field in use somewhere, frame-aligned audio timing
    // (multiples of 1/30 s) as NarrationService produces it, real catalog asset ids so the
    // renderer's smoke render can use the same file.
    internal static VideoPlan CanonicalPlan()
    {
        Scene S(int order, SceneType type, double seconds, double? spoken, string? narration, string? text, string asset,
                AnimationId animation, SfxId? sfx = null, string? emphasis = null, string? secondary = null, IReadOnlyList<string>? items = null) =>
            new($"s{order}", order, type, seconds, narration, text, [asset], animation, sfx, null, emphasis, spoken, secondary, items);

        var scenes = new List<Scene>
        {
            S(1, SceneType.Hook, 3.4, 3.1, "Stuck at the same number of pull-ups for months?", "Stuck at 5 pull-ups?", "mascot-thinking", AnimationId.PunchZoom, SfxId.Whoosh, "same number"),
            S(2, SceneType.MythVsFact, 4.2, 3.9, "Doing max sets every day feels productive, but it wears you down.", "Max out daily", "mascot-tired", AnimationId.Shake,
              emphasis: "wears you down", secondary: "Leave reps in reserve"),
            S(3, SceneType.ExerciseDemo, 3.8, 3.5, "Use slow negatives: lower yourself for three to five seconds.", "Slow negatives", "pullup-top", AnimationId.Slide, emphasis: "slow negatives"),
            S(4, SceneType.AnatomyHighlight, 3.2, 2.9, "Your lats do most of the pulling.", "Lats do the work", "lats-highlight", AnimationId.Highlight, emphasis: "lats"),
            S(5, SceneType.Comparison, 3.6, 3.3, "Half reps build half strength; full range builds all of it.", "Half reps", "squat-bottom", AnimationId.Pop,
              SfxId.Pop, "full range", "Full range"),
            S(6, SceneType.List, 4.0, 3.7, "Train three days a week, add one rep at a time, and sleep well.", "The plan", "calendar", AnimationId.Fade,
              items: ["3 days a week", "+1 rep", "Sleep 7-9 h"]),
            S(7, SceneType.Progress, 3.0, 2.7, "Most people add a rep every week or two.", "+1 rep / 2 weeks", "mascot-happy", AnimationId.Counter, emphasis: "every week",
              secondary: "a typical pace"),
            S(8, SceneType.Recovery, 3.4, 3.1, "Rest days are when the muscle actually adapts.", "Rest = growth", "sleep", AnimationId.Pulse,
              items: ["Sleep", "Protein"]),
            S(9, SceneType.Outro, 2.0, null, null, "Follow for more", "mascot-happy", AnimationId.Bounce, SfxId.Success),
        };

        return new VideoPlan(
            "Why Your Pull-Ups Aren't Increasing",
            "Stuck at the same number of pull-ups for months?",
            scenes.Sum(s => s.DurationSeconds),
            string.Join(' ', scenes.Where(s => s.Narration is not null).Select(s => s.Narration)),
            scenes,
            "default",
            "bold",
            "upbeat",
            Takeaway: "Fewer, better reps beat daily max sets.",
            ReviewWarnings: ["Example warning for the reviewer."],
            NarrationOutdated: false);
    }
}
