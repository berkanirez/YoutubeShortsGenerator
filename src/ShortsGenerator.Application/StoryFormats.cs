using System.Text.RegularExpressions;

namespace ShortsGenerator.Application;

// How a Short tells its lesson. Plain "here are the facts" narration loses viewers; the
// formats that hold attention on fitness Shorts are story shapes - a character with a problem
// and a turning point, a countdown that saves the best for last, a ranking the viewer places
// themselves in. These are generic genres, written here in our own words; no creator's
// scripts, catchphrases or jokes are reused (CLAUDE.md section 4).
public enum StoryFormat
{
    // One guy, his goal, what kept failing, the turning point, the result - then the lesson.
    Journey,

    // Three things counted down, the most surprising saved for number one and teased early.
    Countdown,

    // What happens inside the body, in time order, like a slow-motion replay.
    InsideTheBody,

    // Levels 1-4 the viewer places themselves in.
    Levels,

    // A belief, the moment it falls apart, the real explanation.
    MythStory,
}

public static partial class StoryFormats
{
    // The topic says which shape fits ("... Levels Ranked", "What Happens When ...",
    // "3 Mistakes ..."); otherwise a stable pick per topic, so the same topic always gets the
    // same format and different topics spread across the general-purpose ones.
    public static StoryFormat Pick(string topic) => Pick(topic, []);

    /// <summary>The shape the topic itself asks for, or null for a general topic.</summary>
    public static StoryFormat? Explicit(string topic)
    {
        var t = topic.ToLowerInvariant();
        if (LevelsWords().IsMatch(t))
        {
            return StoryFormat.Levels;
        }

        if (InsideWords().IsMatch(t))
        {
            return StoryFormat.InsideTheBody;
        }

        if (CountdownWords().IsMatch(t))
        {
            return StoryFormat.Countdown;
        }

        return MythWords().IsMatch(t) ? StoryFormat.MythStory : null;
    }

    /// <param name="recentScripts">The latest videos' narrations, most recent first.</param>
    public static StoryFormat Pick(string topic, IReadOnlyList<string> recentScripts)
    {
        var t = topic.ToLowerInvariant();
        if (Explicit(topic) is { } asked)
        {
            return asked;
        }

        // Topics that don't call for a shape get the general format used least recently, so
        // three videos in a row aren't all countdowns.
        StoryFormat[] general = [StoryFormat.Journey, StoryFormat.Countdown, StoryFormat.MythStory];
        var recent = recentScripts.Take(4).Select(Detect).ToList();
        var offset = (int)(StableHash(t) % (uint)general.Length);
        return general
            .Select((format, i) => (format, score: recent.Select((r, k) => r == format ? 4 - k : 0).Sum(), tie: (i + offset) % general.Length))
            .OrderBy(c => c.score)
            .ThenBy(c => c.tie)
            .First()
            .format;
    }

    // Which shape a finished narration has, from the phrases each shape produces. Null when it
    // isn't recognizable - then it simply doesn't count against any format.
    public static StoryFormat? Detect(string script)
    {
        var s = script.ToLowerInvariant();
        if (s.Contains("number three") || s.Contains("number two") || s.Contains("number one:"))
        {
            return StoryFormat.Countdown;
        }

        if (s.Contains("level one") || s.Contains("level four") || s.Contains("level 1"))
        {
            return StoryFormat.Levels;
        }

        if (s.Contains("this guy"))
        {
            return StoryFormat.Journey;
        }

        if (s.Contains("most people believe") || s.Contains("myth") || s.Contains("believed"))
        {
            return StoryFormat.MythStory;
        }

        if (s.Contains("first few seconds") || s.Contains("hours later") || s.Contains("days after") || s.Contains("days later"))
        {
            return StoryFormat.InsideTheBody;
        }

        return null;
    }

    public static string Instructions(StoryFormat format) => format switch
    {
        StoryFormat.Journey =>
            "Tell it as a mini story about one guy who trains (\"this guy\" or \"he\" - no name): what he kept doing, " +
            "how stuck he felt, the one thing he changed, what happened after - then turn to the viewer. The lesson must be accurate.",
        StoryFormat.Countdown =>
            "Go through three things - number one, number two, number three - saving the most surprising for number three, " +
            "and tease it in the second sentence. Each thing gets two short sentences: the mistake, then the fix.",
        StoryFormat.InsideTheBody =>
            "Walk through what happens inside the body in time order - the first seconds, the hours after, the days after - " +
            "like a slow-motion replay, then what it means for their training.",
        StoryFormat.Levels =>
            "Rank it as levels one to four, beginner to advanced, one or two short sentences each, and end by asking " +
            "which level they are at.",
        _ =>
            "Start from what most people believe, then the moment it falls apart for someone who believed it, then the " +
            "real explanation and what to do instead.",
    };

    private static uint StableHash(string text)
    {
        var hash = 2166136261u;
        foreach (var c in text)
        {
            hash = (hash ^ c) * 16777619u;
        }

        return hash;
    }

    [GeneratedRegex(@"\b(levels?|ranked|tier|which one are you)\b")]
    private static partial Regex LevelsWords();

    [GeneratedRegex(@"\b(what happens|what happen|inside your body|happens to your)\b")]
    private static partial Regex InsideWords();

    [GeneratedRegex(@"\b(mistakes?|reasons|ways|things|signs|tips)\b")]
    private static partial Regex CountdownWords();

    [GeneratedRegex(@"\b(myths?|truth|lie|really|actually|does|is .* bad|bad for)\b")]
    private static partial Regex MythWords();
}
