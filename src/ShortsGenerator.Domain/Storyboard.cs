namespace ShortsGenerator.Domain;

public enum StoryboardFormat
{
    // "3 push-up mistakes": the mistakes one by one (number one, two, three - the most
    // important saved for last), each shown wrong then fixed.
    Countdown,

    // "Pull-up levels": a ladder of exercises from easy to hard, "which one are you?".
    Levels,
}

/// <summary>
/// What a storyboard video says, beat by beat - written by the LLM from controlled lists
/// (mistake ids, a ladder), turned into scenes by <see cref="StoryboardComposer"/>. Kept in
/// the plan so the scenes can be composed again when new art arrives.
/// </summary>
/// <param name="Items">Countdown: the mistakes in spoken order (number one first, the most important last). Levels: the ladder's steps, easiest first.</param>
/// <param name="Ladder">Levels: the ladder id (<see cref="LevelLadders"/>).</param>
public sealed record Storyboard(
    StoryboardFormat Format,
    string Hook,
    string Intro,
    IReadOnlyList<StoryboardItem> Items,
    string? Payoff,
    string? PayoffWord,
    string Cta,
    string? Ladder = null);

/// <param name="Subject">Countdown: the mistake subject id (subjects.json, kind Mistake). Levels: the exercise subject id.</param>
/// <param name="Line">Countdown: what goes wrong. Levels: what this level looks like.</param>
/// <param name="Fix">Countdown: how to fix it. Levels: unused.</param>
/// <param name="Label">A mistake that isn't in subjects.json ("other", from an owner's script): its name on screen.</param>
/// <param name="FixLabel">Same: the fix on screen.</param>
public sealed record StoryboardItem(string Subject, string Line, string? Fix = null, string? Label = null, string? FixLabel = null);
