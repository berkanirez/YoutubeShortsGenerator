namespace ShortsGenerator.Domain.Tests;

public class ArtSheetPlannerTests
{
    private static ArtSubject Pose(int i) => new($"mascot-p{i}", ArtKind.MascotPose, ["p"], $"pose {i}");
    private static ArtSubject Icon(int i) => new($"icon{i}", ArtKind.Icon, ["i"], $"icon {i}");
    private static ArtSubject Pair(int i) => new($"ex{i}", ArtKind.ExercisePair, ["e"], $"exercise {i}", Top: "up", Bottom: "down");

    [Fact]
    public void SeventeenExpressions_BecomeFiveBalancedSheetsOfCharactersInARow()
    {
        var (sheets, singles) = ArtSheetPlanner.Plan(Enumerable.Range(1, 17).Select(Pose).ToList());

        Assert.Empty(singles);
        Assert.Equal([4, 4, 3, 3, 3], sheets.Select(s => s.Subjects.Count));
        Assert.All(sheets, s => Assert.Equal((1, s.Subjects.Count), (s.Rows, s.Cols)));
        Assert.Contains("Panel 1 (far left): pose 1.", sheets[0].Prompt);
        Assert.Contains("Panel 4 (far right): pose 4.", sheets[0].Prompt);
        Assert.Equal("sheet-mascot-p1.png", sheets[0].FileName);
    }

    [Fact]
    public void AnAnimation_IsFourFramesOfItsExercise_HiddenFromTheClassicPlanner_AndAlwaysItsOwnSheet()
    {
        var cycle = new ArtSubject("pushup-cycle", ArtKind.Cycle, ["pushup cycle"], "push-up rep", Top: "arms straight", Bottom: "chest near the floor", Exercise: "pushup");
        var gesture = new ArtSubject("mascot-point", ArtKind.Cycle, ["pointing gesture"], "gesture", Top: "arms down", Bottom: "pointing");

        Assert.Equal(["pushup-f1", "pushup-f2", "pushup-f3", "pushup-f4"], cycle.AssetIds);
        Assert.Equal("pushup-cycle.png", cycle.IncomingFileName);
        Assert.Equal("mascot-point-f1", gesture.AssetIds[0]);
        Assert.Equal("mascot-point-anim.png", gesture.IncomingFileName);
        Assert.Contains(ArtSubject.WrongFormTag, cycle.Tags);
        var prompt = ArtPromptBuilder.Build(cycle);
        Assert.Contains("Top-left (frame 1): arms straight.", prompt);
        Assert.Contains("Bottom-right (frame 4): chest near the floor.", prompt);

        var (sheets, singles) = ArtSheetPlanner.Plan([cycle, gesture]);
        Assert.Empty(sheets);
        Assert.Equal(2, singles.Count);
    }

    [Fact]
    public void IconsGoNineToAGrid_AndALoneLeftoverStaysASinglePrompt()
    {
        var (sheets, _) = ArtSheetPlanner.Plan(Enumerable.Range(1, 9).Select(Icon).ToList());
        var sheet = Assert.Single(sheets);
        Assert.Equal((3, 3), (sheet.Rows, sheet.Cols));
        Assert.Contains("9 (bottom right): icon 9.", sheet.Prompt);

        var (pairSheets, singles) = ArtSheetPlanner.Plan(Enumerable.Range(1, 3).Select(Pair).ToList());
        Assert.Equal((2, 1), (pairSheets.Single().Rows, pairSheets.Single().Cols));
        Assert.Equal("ex3", singles.Single().Id);
    }
}
