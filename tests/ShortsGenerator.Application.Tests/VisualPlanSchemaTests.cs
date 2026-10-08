using System.Text.Json;

namespace ShortsGenerator.Application.Tests;

public class VisualPlanSchemaTests
{
    [Fact]
    public void Build_PinsTheSceneCountExactly()
    {
        var scenes = Parse(VisualPlanSchema.Build(7, ["dumbbell"])).GetProperty("properties").GetProperty("scenes");

        Assert.Equal(7, scenes.GetProperty("minItems").GetInt32());
        Assert.Equal(7, scenes.GetProperty("maxItems").GetInt32());
    }

    [Fact]
    public void Build_OnlyAllowsCatalogAssetIdsAndControlledVocabularies()
    {
        var item = SceneItem(VisualPlanSchema.Build(6, ["dumbbell", "pullup-bar"]));

        Assert.Equal(["dumbbell", "pullup-bar"], Strings(item.GetProperty("assetIds").GetProperty("items").GetProperty("enum")));
        Assert.Contains("MythVsFact", Strings(item.GetProperty("type").GetProperty("enum")));
        Assert.Contains("punchZoom", Strings(item.GetProperty("animation").GetProperty("enum")));
        Assert.Contains("whoosh", Strings(item.GetProperty("sfx").GetProperty("enum")));
    }

    [Fact]
    public void Build_MusicMoodIsRequiredAndLimitedToTheGeneratedTracks()
    {
        var root = Parse(VisualPlanSchema.Build(6, ["dumbbell"]));

        Assert.Equal(["upbeat", "calm"], Strings(root.GetProperty("properties").GetProperty("musicMood").GetProperty("enum")));
        Assert.Contains("musicMood", Strings(root.GetProperty("required")));
    }

    [Fact]
    public void Build_SfxMayBeNull()
    {
        var sfx = SceneItem(VisualPlanSchema.Build(6, ["dumbbell"])).GetProperty("sfx").GetProperty("enum");

        Assert.Contains(sfx.EnumerateArray(), value => value.ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public void Build_EmptyCatalog_ForcesEmptyAssetListInsteadOfAnUnsatisfiableEnum()
    {
        var assetIds = SceneItem(VisualPlanSchema.Build(6, [])).GetProperty("assetIds");

        Assert.Equal(0, assetIds.GetProperty("maxItems").GetInt32());
        Assert.False(assetIds.TryGetProperty("items", out _));
    }

    [Fact]
    public async Task ScenePlanningService_SendsASchemaMatchingTheSplitSceneCount()
    {
        var repository = new InMemoryVideoJobRepository();
        var now = DateTimeOffset.UtcNow;
        var job = ShortsGenerator.Domain.VideoJob.Create("Pull-ups", null, now);
        job.MarkPlanning(now);
        job.AttachGeneratedScript(TestNarrations.PullUps, now);
        await repository.AddAsync(job, CancellationToken.None);
        var llm = new FakeLlmProvider(VisualPlanJson.For(TestNarrations.PullUps));

        await new ScenePlanningService(llm, repository, new InMemoryVideoPlanStore(), new InMemoryAssetRepository(), TimeProvider.System)
            .GeneratePlanAsync(job.Id, CancellationToken.None);

        var scenes = Parse(Assert.Single(llm.ReceivedSchemas)).GetProperty("properties").GetProperty("scenes");
        Assert.Equal(7, scenes.GetProperty("maxItems").GetInt32());
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    private static JsonElement SceneItem(string schema) =>
        Parse(schema).GetProperty("properties").GetProperty("scenes").GetProperty("items").GetProperty("properties");

    private static IEnumerable<string?> Strings(JsonElement array) =>
        array.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString());
}
