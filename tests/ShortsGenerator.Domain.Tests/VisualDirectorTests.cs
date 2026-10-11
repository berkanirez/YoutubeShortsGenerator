namespace ShortsGenerator.Domain.Tests;

public class VisualDirectorTests
{
    private static readonly string[] Catalog =
    [
        "mascot-idle", "mascot-thinking", "mascot-tired", "mascot-happy", "mascot-flex", "mascot-stop",
        "mascot-thumbsup", "mascot-pointing", "mascot-shocked", "mascot-sleeping", "pullup-top", "lats-highlight",
    ];

    private static VideoPlan Plan(params Scene[] scenes) => new("T", "H", 30, "n", scenes, null, null, null);

    private static Scene S(int order, SceneType type, params string[] assets) =>
        new($"s{order}", order, type, 4, "words", null, assets, AnimationId.Pop, null, null, null);

    private static VideoPlan Typical() => Plan(
        S(1, SceneType.Hook),
        S(2, SceneType.AnatomyHighlight, "lats-highlight"),
        S(3, SceneType.MythVsFact, "mascot-thinking"),
        S(4, SceneType.Outro));

    [Fact]
    public void SubjectArt_IsNeverReplaced_OnlyMascotSlotsAreDirected()
    {
        var directed = VisualDirector.Direct(Typical(), [], Catalog, seed: 0);

        Assert.Equal(["lats-highlight"], directed.Scenes[1].AssetIds);
        Assert.StartsWith("mascot-", directed.Scenes[0].AssetIds.Single());
        Assert.StartsWith("mascot-", directed.Scenes[3].AssetIds.Single());
    }

    [Fact]
    public void TheHookExpression_RotatesAcrossVideos_InsteadOfRepeating()
    {
        var made = new List<VideoPlan>();
        for (var i = 0; i < 5; i++)
        {
            made.Insert(0, VisualDirector.Direct(Typical(), made, Catalog, seed: 7));
        }

        // Five videos in a row, five different hook expressions (the pool has five).
        Assert.Equal(5, made.Select(p => p.Scenes[0].AssetIds.Single()).Distinct().Count());
        Assert.Equal(5, made.Select(p => p.Scenes[0].Animation).Distinct().Count());
    }

    [Fact]
    public void TheLookSet_RotatesToo()
    {
        var made = new List<VideoPlan>();
        for (var i = 0; i < VisualDirector.VariantCount; i++)
        {
            made.Insert(0, VisualDirector.Direct(Typical(), made, Catalog, seed: 3));
        }

        Assert.Equal(VisualDirector.VariantCount, made.Select(p => p.Variant).Distinct().Count());
    }

    [Fact]
    public void WithinAVideo_TheSameExpressionIsNotShownTwiceInARow()
    {
        var plan = Plan(S(1, SceneType.Hook), S(2, SceneType.Outro));

        var directed = VisualDirector.Direct(plan, [], ["mascot-pointing", "mascot-happy"], seed: 0);

        Assert.NotEqual(directed.Scenes[0].AssetIds.Single(), directed.Scenes[1].AssetIds.Single());
    }

    [Fact]
    public void SameJobAndHistory_GiveTheSamePlan()
    {
        var a = VisualDirector.Direct(Typical(), [], Catalog, seed: 11);
        var b = VisualDirector.Direct(Typical(), [], Catalog, seed: 11);

        Assert.Equal(a.Scenes.Select(s => s.AssetIds.Single()), b.Scenes.Select(s => s.AssetIds.Single()));
        Assert.Equal(a.Variant, b.Variant);
    }
}

public class VisualDirectorStoryboardTests
{
    private static readonly string[] Catalog = ["mascot-stop", "mascot-crossed-arms", "mascot-pointing", "mascot-idea", "mascot-shocked", "mascot-facepalm", "mascot-flex"];

    private static VideoPlan Countdown(bool withWrongArt) => new("T", "H", 30, "n",
    [
        new("s1", 1, SceneType.Hook, 3, "hook", null, [], AnimationId.Fade, null, null, null, Shot: ShotType.HookStop,
            AltAssetIds: withWrongArt ? ["pushup-hips-sag", "pushup-top"] : null),
        new("s2", 2, SceneType.MythVsFact, 3, "number three", null, [], AnimationId.Fade, null, null, null, Shot: ShotType.MistakeReaction),
        new("s3", 3, SceneType.Outro, 3, "cta", null, [], AnimationId.Fade, null, null, null, Shot: ShotType.CtaLoop),
    ], null, null, null);

    [Fact]
    public void TheCountdownHookShot_RotatesAcrossVideos()
    {
        var made = new List<VideoPlan>();
        for (var i = 0; i < 3; i++)
        {
            made.Insert(0, VisualDirector.Direct(Countdown(false), made, Catalog, seed: 1));
        }

        Assert.Equal(3, made.Select(p => p.Scenes[0].Shot).Distinct().Count());
        Assert.DoesNotContain(made, p => p.Scenes[0].Shot == ShotType.HookQuestion);   // no wrong-form art: no "which one?"
    }

    [Fact]
    public void ShotsGetExpressionsThatFitThem()
    {
        var directed = VisualDirector.Direct(Countdown(false), [], Catalog, seed: 0);

        Assert.Contains(directed.Scenes[1].AssetIds.Single(), new[] { "mascot-facepalm", "mascot-stop", "mascot-shocked" });
        Assert.Equal("mascot-pointing", directed.Scenes[2].AssetIds.Single());
    }
}
