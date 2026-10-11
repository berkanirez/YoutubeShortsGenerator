using System.Text.RegularExpressions;

namespace ShortsGenerator.Domain;

/// <summary>One step of a ladder: an exercise subject (subjects.json), its spoken name, and the benchmark that
/// marks the level - the LLM only puts it into words, so a level never says something made up.</summary>
public sealed record LadderStep(string Subject, string Name, string Benchmark);

public sealed record LevelLadder(string Id, IReadOnlyList<string> Keywords, IReadOnlyList<LadderStep> Steps);

// The progressions a Levels video walks through, easiest first - only exercises the art
// library has (every step is a subject with art), so a levels video never waits for pictures.
// Picked in code from the topic's words, not by the LLM: a 3B model happily invents a level.
public static partial class LevelLadders
{
    public static readonly IReadOnlyList<LevelLadder> All =
    [
        new("core", ["core", "abs", "ab", "plank", "six pack", "stomach"],
            [new("plank", "the plank", "a straight-line plank held for 60 seconds"),
             new("side-plank", "the side plank", "30 seconds on each side without the hips dropping"),
             new("leg-raise", "the hanging leg raise", "10 slow reps with straight legs up to hip height"),
             new("l-sit", "the L-sit", "legs held straight out in front for 10 seconds")]),
        new("push", ["push", "push up", "pushup", "chest", "press", "handstand"],
            [new("pushup", "the push-up", "20 clean push-ups with the chest to the floor"),
             new("dip", "the dip", "10 full dips with the shoulders kept down"),
             new("handstand", "the handstand", "a handstand held against a wall for 30 seconds")]),
        new("legs", ["leg", "legs", "squat", "glute", "glutes", "lower body", "quad", "quads"],
            [new("wall-sit", "the wall sit", "60 seconds with the thighs parallel to the floor"),
             new("squat", "the squat", "20 deep bodyweight squats with the heels down"),
             new("lunge", "the lunge", "10 controlled lunges on each leg"),
             new("deadlift", "the deadlift", "lifting your bodyweight on the bar with a flat back")]),
        new("arms", ["arm", "arms", "bicep", "biceps", "tricep", "triceps", "forearm"],
            [new("curl", "the dumbbell curl", "8 strict curls with the elbows pinned"),
             new("tricep-extension", "the triceps extension", "10 slow overhead extensions"),
             new("dip", "the dip", "10 full dips"),
             new("chin-up", "the chin-up", "8 strict chin-ups from a dead hang")]),
        new("back", ["back", "lat", "lats", "row", "v shape", "posture"],
            [new("row", "the barbell row", "12 rows with a flat back"),
             new("dead-hang", "the dead hang", "hanging from the bar for 60 seconds"),
             new("pullup", "the pull-up", "8 strict pull-ups"),
             new("muscle-up", "the muscle-up", "one clean muscle-up over the bar")]),
        new("pull", ["pull", "pull up", "pullup", "chin up", "hang", "calisthenics", "grip", "strength", "physique", "fitness"],
            [new("dead-hang", "the dead hang", "hanging from the bar for 30 seconds"),
             new("chin-up", "the chin-up", "5 chin-ups from a dead hang"),
             new("pullup", "the pull-up", "10 strict pull-ups, chin over the bar"),
             new("muscle-up", "the muscle-up", "one clean muscle-up over the bar")]),
    ];

    /// <summary>The ladder whose keyword appears earliest in the topic; pull-ups when none does.</summary>
    public static LevelLadder Pick(string topic)
    {
        var text = " " + NonWord().Replace(topic.ToLowerInvariant(), " ") + " ";
        return All
            .Select(l => (Ladder: l, Position: l.Keywords
                .Select(k => text.IndexOf(" " + k + " ", StringComparison.Ordinal))
                .Where(i => i >= 0)
                .DefaultIfEmpty(int.MaxValue)
                .Min()))
            .OrderBy(x => x.Position)
            .Select(x => x.Position == int.MaxValue ? All.Single(l => l.Id == "pull") : x.Ladder)
            .First();
    }

    public static LevelLadder? Find(string? id) => All.FirstOrDefault(l => l.Id == id);

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonWord();
}
