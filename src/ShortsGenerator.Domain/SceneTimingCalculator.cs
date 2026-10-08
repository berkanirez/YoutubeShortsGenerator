namespace ShortsGenerator.Domain;

/// <summary>Where one narrated scene's speech sits inside narration.wav.</summary>
public sealed record NarrationSegmentTiming(double StartSeconds, double DurationSeconds);

// Turns "the audio says scene 3 is spoken from 9.41s to 13.02s" into scene durations the
// renderer can play frame-exactly (CLAUDE.md Day 12: visual timeline matches narration,
// no drift, deterministic).
public static class SceneTimingCalculator
{
    // A silent closing card (Outro without narration) still needs to be readable.
    public const double MinSilentSceneSeconds = 1.5;
    public const double MaxSilentSceneSeconds = 4.0;

    /// <param name="plan">The plan whose scenes were narrated, in order.</param>
    /// <param name="segments">One timing per scene that has narration, in scene order.</param>
    /// <param name="audioDurationSeconds">Total length of narration.wav (includes the trailing pause).</param>
    public static VideoPlan Apply(
        VideoPlan plan, IReadOnlyList<NarrationSegmentTiming> segments, double audioDurationSeconds, int fps)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fps);
        var scenes = plan.Scenes.OrderBy(s => s.Order).ToList();
        var narrated = scenes.Where(HasNarration).ToList();

        if (narrated.Count != segments.Count)
        {
            throw new ArgumentException(
                $"Plan has {narrated.Count} narrated scenes but the audio has {segments.Count} segments.",
                nameof(segments));
        }

        // Silent scenes are only placed after the speech: a silent scene in the middle would
        // need silence spliced into the audio. VideoPlanValidator only allows the Outro to be
        // silent, so this is a guard against misuse, not a real-world case.
        var firstSilentIndex = scenes.FindIndex(s => !HasNarration(s));
        if (firstSilentIndex >= 0 && scenes.Skip(firstSilentIndex).Any(HasNarration))
        {
            throw new ArgumentException("Scenes without narration must come after every narrated scene.", nameof(plan));
        }

        // Scene boundaries in seconds: each narrated scene starts where its speech starts
        // and runs until the next one's speech starts (so the pause after a sentence belongs
        // to the scene that said it). The first scene starts at 0 - not at the first
        // segment's start - so there's never a blank frame before the hook.
        var boundaries = new List<double> { 0 };
        for (var i = 1; i < segments.Count; i++)
        {
            boundaries.Add(segments[i].StartSeconds);
        }

        var lastNarratedEnd = segments.Count > 0
            ? Math.Max(audioDurationSeconds, segments[^1].StartSeconds + segments[^1].DurationSeconds)
            : 0;
        boundaries.Add(lastNarratedEnd);

        var silentEnd = lastNarratedEnd;
        foreach (var silent in scenes.Skip(narrated.Count))
        {
            silentEnd += Math.Clamp(silent.DurationSeconds, MinSilentSceneSeconds, MaxSilentSceneSeconds);
            boundaries.Add(silentEnd);
        }

        // Round the *boundaries* to frames, then take differences - never round each
        // duration on its own. Per-scene rounding drifts by up to half a frame per scene;
        // this way every scene starts on exactly the frame its audio starts on.
        var frames = boundaries.Select(seconds => (int)Math.Round(seconds * fps, MidpointRounding.AwayFromZero)).ToList();

        var timedScenes = new List<Scene>(scenes.Count);
        for (var i = 0; i < scenes.Count; i++)
        {
            var durationFrames = Math.Max(1, frames[i + 1] - frames[i]);
            timedScenes.Add(scenes[i] with
            {
                DurationSeconds = (double)durationFrames / fps,
                SpokenDurationSeconds = i < segments.Count ? segments[i].DurationSeconds : null,
            });
        }

        return plan with
        {
            Scenes = timedScenes,
            TargetDurationSeconds = timedScenes.Sum(s => s.DurationSeconds),
        };
    }

    private static bool HasNarration(Scene scene) => !string.IsNullOrWhiteSpace(scene.Narration);
}
