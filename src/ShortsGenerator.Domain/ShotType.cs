namespace ShortsGenerator.Domain;

// The visual vocabulary of a storyboard video (docs/VIDEO_QUALITY_RESEARCH.md section 6B).
// Every shot carries a visual sign - a cross or check, an arrow, a pain point, a number, a
// level meter - so the video is understandable with the sound off. The renderer has one
// component per value; a scene without a shot uses the older per-SceneType templates.
[JsonCamelCaseEnumConverter]
public enum ShotType
{
    // Hooks (first scene). Fast push-in, then:
    HookStop,      // the mascot stops the viewer: giant "STOP" behind, red cross
    HookNumber,    // giant count ("3") and what it counts ("MISTAKES")
    HookShock,     // shocked mascot, shock lines, giant "?"
    HookQuestion,  // the wrong and the right form side by side, unlabelled, "WHICH ONE?"

    KineticWord,     // one giant word behind the mascot ("3 MISTAKES")
    SplitFix,        // wrong form (grey, cross, pain point) above the right form (colour, check)
    MistakeReaction, // no wrong-form drawing yet: reacting mascot, giant "#3", the mistake's name, cross
    FixDemo,         // the right form doing reps, green ring and arrow at the body part that matters
    ZoomDetail,      // the right form with a magnifier on the body part that matters
    Lineup,          // several pictures side by side on the floor (levels), numbered
    LevelShow,       // one level: the exercise doing reps, giant "LEVEL 2", a difficulty meter
    Payoff,          // spotlight and rays behind a proud mascot
    CtaLoop,         // close-up of the mascot pointing at the viewer
}
