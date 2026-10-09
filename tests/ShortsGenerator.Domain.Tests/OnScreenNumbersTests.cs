namespace ShortsGenerator.Domain.Tests;

// Day 29: 25 on-screen numbers in 15 real videos that the narration never said - among them
// "60 minutes of rest = 1% muscle growth" and "90%+ proper form". The cases are real.
public class OnScreenNumbersTests
{
    private static Scene S(SceneType type, string narration, string? text, string? secondary = null, IReadOnlyList<string>? items = null) =>
        new("s4", 4, type, 3, narration, text, [], AnimationId.Pop, null, null, null, SecondaryText: secondary, Items: items);

    [Fact]
    public void AnInventedStatistic_IsReported()
    {
        var scene = S(SceneType.Progress,
            "When you rest, your muscles have time to repair and rebuild, making them more resilient to future challenges.",
            "60 minutes of rest = 1% muscle growth", "The more you rest, the stronger you'll become");

        var issues = OnScreenNumbers.Unsourced(scene);

        Assert.Equal(["60", "1%"], issues);
    }

    [Theory]
    [InlineData("Aim for 3 sets of 8-12 reps to challenge your muscles.", "3 sets of 8-12 reps")]
    [InlineData("Lower yourself for three to five seconds.", "3-5 second negatives")]
    [InlineData("Sleep seven to nine hours a night.", "7-9 hours")]
    [InlineData("Add one rep every week.", "+1 rep a week")]
    [InlineData("Rest for 90 seconds between sets.", "90s rest")]
    public void NumbersTheNarrationSays_InDigitsOrWords_AreFine(string narration, string text)
    {
        Assert.Empty(OnScreenNumbers.Unsourced(S(SceneType.Progress, narration, text)));
    }

    [Fact]
    public void ItemsAndSecondaryTextAreCheckedToo()
    {
        var scene = S(SceneType.List, "But trust us, taking a day off can actually make you stronger.", "Recovery tips",
            items: ["Get 7-9 hours of sleep", "Stretch"]);

        Assert.Equal(["7", "9"], OnScreenNumbers.Unsourced(scene));
    }

    [Fact]
    public void Strip_RemovesOnlyTheFieldsWithUnsourcedNumbers()
    {
        var scene = S(SceneType.Progress, "Rest helps you rebuild.", "60 minutes = 1% growth", "Rest more",
            items: ["Sleep 8 hours", "Eat well"]);

        var stripped = OnScreenNumbers.Strip(scene);

        Assert.Null(stripped.OnScreenText);
        Assert.Equal("Rest more", stripped.SecondaryText);
        Assert.Equal(["Eat well"], stripped.Items);
    }
}
