using ShortsGenerator.Domain;

namespace ShortsGenerator.Application.Tests;

public class AssetMatcherTests
{
    private static readonly IReadOnlyList<AssetMetadata> Catalog =
    [
        A("biceps-highlight", ["biceps", "arms", "curl"], SceneType.AnatomyHighlight),
        A("core-highlight", ["core", "abs", "plank"], SceneType.AnatomyHighlight),
        A("quads-highlight", ["quads", "legs", "squat"], SceneType.AnatomyHighlight),
        A("squat-bottom", ["squat", "depth"], SceneType.ExerciseDemo, SceneType.Comparison),
        A("mascot-thinking", ["mascot", "thinking"], SceneType.Comparison),
        A("pullup-top", ["pull-up", "back"], SceneType.ExerciseDemo),
    ];

    [Fact]
    public void Improve_ChosenAssetUnrelatedToTheScene_IsSwappedForTheMatchingOne()
    {
        // The real Day 20 case: "Core Muscle" scene, model picked biceps-highlight.
        var result = AssetMatcher.Improve(["biceps-highlight"], SceneType.AnatomyHighlight,
            "Core Muscle A strong core keeps your spine stable.", Catalog);

        Assert.Equal(["core-highlight"], result);
    }

    [Fact]
    public void Improve_ChosenAssetThatFits_IsKept_EvenIfAnotherAlsoMatches()
    {
        var result = AssetMatcher.Improve(["quads-highlight"], SceneType.AnatomyHighlight,
            "Squats train your quads and your core.", Catalog);

        Assert.Equal(["quads-highlight"], result);
    }

    [Fact]
    public void Improve_OnlyConsidersAssetsCompatibleWithTheSceneType()
    {
        // squat-bottom is compatible with Comparison; quads-highlight (also tagged squat) is not.
        var result = AssetMatcher.Improve(["mascot-thinking"], SceneType.Comparison,
            "Keep your knees in line as you squat.", Catalog);

        Assert.Equal(["squat-bottom"], result);
    }

    [Fact]
    public void Improve_NoMatchAnywhere_KeepsTheModelsChoice()
    {
        var result = AssetMatcher.Improve(["biceps-highlight"], SceneType.AnatomyHighlight,
            "Breathe out on the way up.", Catalog);

        Assert.Equal(["biceps-highlight"], result);
    }

    [Fact]
    public void Improve_EmptyChoice_GetsAMatchOnlyWhenOneExists()
    {
        Assert.Equal(["pullup-top"], AssetMatcher.Improve([], SceneType.ExerciseDemo, "Do strict pull-ups.", Catalog));
        Assert.Empty(AssetMatcher.Improve([], SceneType.ExerciseDemo, "Breathe out.", Catalog));
    }

    [Fact]
    public void Improve_SimplePluralsAndHyphenatedTagsMatch()
    {
        Assert.Equal(["pullup-top"], AssetMatcher.Improve(["squat-bottom"], SceneType.ExerciseDemo, "More pull-ups, fewer excuses.", Catalog));
    }

    private static AssetMetadata A(string id, string[] tags, params SceneType[] types) =>
        new(id, $"../x/{id}.svg", "anatomy", tags, types, AssetOrientation.Square, null);
}
