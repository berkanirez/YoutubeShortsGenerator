namespace ShortsGenerator.Domain.Tests;

public class SceneTimingCalculatorTests
{
    private const int Fps = 30;

    [Fact]
    public void Apply_SceneBoundariesFollowTheAudioSegments()
    {
        var plan = Plan(Narrated("a", 1), Narrated("b", 2), Narrated("c", 3));
        NarrationSegmentTiming[] segments = [new(0.0, 2.0), new(2.3, 1.5), new(4.1, 3.0)];

        var timed = SceneTimingCalculator.Apply(plan, segments, audioDurationSeconds: 7.4, Fps);

        // scene 1: 0 -> 2.3, scene 2: 2.3 -> 4.1, scene 3: 4.1 -> 7.4 (end of audio)
        Assert.Equal([69, 54, 99], timed.Scenes.Select(s => Frames(s.DurationSeconds)));
        Assert.Equal(7.4, timed.TargetDurationSeconds, precision: 9);
    }

    [Fact]
    public void Apply_FirstSceneStartsAtZeroEvenIfSpeechStartsLater()
    {
        var plan = Plan(Narrated("a", 1), Narrated("b", 2));
        NarrationSegmentTiming[] segments = [new(0.2, 1.0), new(1.5, 1.0)];

        var timed = SceneTimingCalculator.Apply(plan, segments, 2.8, Fps);

        Assert.Equal(1.5, timed.Scenes[0].DurationSeconds, precision: 9);
    }

    [Fact]
    public void Apply_SetsSpokenDurationFromTheSegment()
    {
        var plan = Plan(Narrated("a", 1), Narrated("b", 2));
        NarrationSegmentTiming[] segments = [new(0, 2.0053), new(2.3053, 1.664)];

        var timed = SceneTimingCalculator.Apply(plan, segments, 4.2693, Fps);

        Assert.Equal([2.0053, 1.664], timed.Scenes.Select(s => s.SpokenDurationSeconds!.Value));
    }

    [Fact]
    public void Apply_SilentOutroGoesAfterTheAudio_WithClampedDuration()
    {
        var plan = Plan(Narrated("a", 1), Narrated("b", 2), Silent("outro", 3, plannedSeconds: 10));
        NarrationSegmentTiming[] segments = [new(0, 2), new(2.3, 2)];

        var timed = SceneTimingCalculator.Apply(plan, segments, 4.6, Fps);

        var outro = timed.Scenes[2];
        Assert.Equal(SceneTimingCalculator.MaxSilentSceneSeconds, outro.DurationSeconds, precision: 9);
        Assert.Null(outro.SpokenDurationSeconds);
        Assert.Equal(4.6 + SceneTimingCalculator.MaxSilentSceneSeconds, timed.TargetDurationSeconds, precision: 9);
    }

    [Fact]
    public void Apply_ManyAwkwardSegments_NoDrift_EverySceneStartsOnItsAudioFrame()
    {
        // 10 scenes whose boundaries all fall between frames (x.xx7s): rounding each
        // duration separately would accumulate drift; rounding boundaries can't.
        var scenes = Enumerable.Range(1, 10).Select(i => Narrated($"s{i}", i)).ToArray();
        var segments = Enumerable.Range(0, 10)
            .Select(i => new NarrationSegmentTiming(i * 3.317, 3.0))
            .ToList();
        const double audioDuration = 10 * 3.317;

        var timed = SceneTimingCalculator.Apply(Plan(scenes), segments, audioDuration, Fps);

        var startFrame = 0;
        for (var i = 0; i < 10; i++)
        {
            Assert.Equal((int)Math.Round(segments[i].StartSeconds * Fps, MidpointRounding.AwayFromZero), startFrame);
            startFrame += Frames(timed.Scenes[i].DurationSeconds);
        }

        Assert.Equal((int)Math.Round(audioDuration * Fps), startFrame);
    }

    [Fact]
    public void Apply_DurationsAreWholeFrames()
    {
        var plan = Plan(Narrated("a", 1), Narrated("b", 2));
        NarrationSegmentTiming[] segments = [new(0, 1.234), new(1.534, 2.111)];

        var timed = SceneTimingCalculator.Apply(plan, segments, 3.945, Fps);

        foreach (var scene in timed.Scenes)
        {
            var frames = scene.DurationSeconds * Fps;
            Assert.Equal(Math.Round(frames), frames, precision: 9);
        }
    }

    [Fact]
    public void Apply_SegmentCountMismatch_Throws()
    {
        var plan = Plan(Narrated("a", 1), Narrated("b", 2));

        Assert.Throws<ArgumentException>(() =>
            SceneTimingCalculator.Apply(plan, [new(0, 1)], 1.3, Fps));
    }

    [Fact]
    public void Apply_SilentSceneBeforeANarratedOne_Throws()
    {
        var plan = Plan(Narrated("a", 1), Silent("gap", 2, 2), Narrated("b", 3));

        Assert.Throws<ArgumentException>(() =>
            SceneTimingCalculator.Apply(plan, [new(0, 1), new(1.3, 1)], 2.6, Fps));
    }

    [Fact]
    public void Apply_UsesSceneOrderNotListOrder()
    {
        var plan = Plan(Narrated("second", 2), Narrated("first", 1));
        NarrationSegmentTiming[] segments = [new(0, 1), new(1.5, 1)];

        var timed = SceneTimingCalculator.Apply(plan, segments, 2.8, Fps);

        Assert.Equal(["first", "second"], timed.Scenes.Select(s => s.Id));
        Assert.Equal(1.5, timed.Scenes[0].DurationSeconds, precision: 9);
    }

    private static int Frames(double seconds) => (int)Math.Round(seconds * Fps);

    private static VideoPlan Plan(params Scene[] scenes) =>
        new("Title", "Hook", 30, "text", scenes, null, null, null);

    private static Scene Narrated(string id, int order) =>
        new(id, order, SceneType.ExerciseDemo, 5, $"Narration {id}.", null, [], AnimationId.Fade, null, null, null);

    private static Scene Silent(string id, int order, double plannedSeconds) =>
        new(id, order, SceneType.Outro, plannedSeconds, null, null, [], AnimationId.Fade, null, null, null);
}
