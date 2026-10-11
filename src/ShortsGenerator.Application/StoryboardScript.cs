using System.Text;
using System.Text.RegularExpressions;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Application;

// An owner-written storyboard: a script written elsewhere (ChatGPT, docs/CHATGPT_SCRIPT_PROMPT.md)
// in a small labelled format, so a better writer than the local 3B model can drive the
// storyboard shots (wrong-vs-right split, level meter) directly - no LLM involved here.
//
//   FORMAT: mistakes                       FORMAT: levels
//   HOOK: <first sentence>                 LADDER: pull            (optional; else from the topic)
//   INTRO: <sentence>                      HOOK: ...
//   1: <mistake id> | <problem> | <fix>    INTRO: ...
//   2: other | <problem> | <fix> | LABEL | FIX LABEL      1: <line>   2: <line> ...
//   PAYOFF: <sentence> | <1-2 words>       CTA: ...
//   CTA: <question>
//
// A plain script (no FORMAT line) keeps working as before.
public static partial class StoryboardScript
{
    public const int MaxHookWords = 14;
    public const int MaxLineWords = 24;

    public static bool IsStructured(string? text) =>
        text is not null && FormatLine().IsMatch(text);

    /// <summary>The storyboard the text describes, or the reasons it can't be used.</summary>
    public static (Storyboard? Storyboard, IReadOnlyList<string> Problems) Parse(string text, string topic, IReadOnlyList<ArtSubject> subjects)
    {
        var problems = new List<string>();
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var items = new SortedDictionary<int, string>();
        foreach (var raw in text.Split('\n'))
        {
            var m = KeyLine().Match(raw);
            if (!m.Success)
            {
                continue;
            }

            var key = m.Groups["key"].Value;
            var value = m.Groups["value"].Value.Trim().Trim('*').Trim();
            if (int.TryParse(key, out var number))
            {
                items[number] = value;
            }
            else
            {
                fields[key.ToUpperInvariant()] = value;
            }
        }

        var format = fields.GetValueOrDefault("FORMAT")?.ToLowerInvariant();
        var hook = Field(fields, "HOOK", MaxHookWords, problems);
        var intro = Field(fields, "INTRO", MaxLineWords, problems);
        var cta = Field(fields, "CTA", MaxLineWords, problems);

        if (format is "mistakes" or "mistake" or "countdown")
        {
            var byId = subjects.Where(s => s.Kind == ArtKind.Mistake).ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
            var parsed = new List<StoryboardItem>();
            foreach (var (number, value) in items)
            {
                var parts = value.Split('|').Select(p => p.Trim()).ToArray();
                if (parts.Length < 3)
                {
                    problems.Add($"Item {number} needs \"<mistake id> | <problem> | <fix>\".");
                    continue;
                }

                var id = parts[0].ToLowerInvariant();
                var line = Sentence(parts[1], $"item {number} problem", problems);
                var fix = Sentence(parts[2], $"item {number} fix", problems);
                if (byId.ContainsKey(id))
                {
                    parsed.Add(new StoryboardItem(byId[id].Id, line, fix));
                }
                else if (id == "other")
                {
                    if (parts.Length < 5 || Words(parts[3]) is < 1 or > 3 || Words(parts[4]) is < 1 or > 3)
                    {
                        problems.Add($"Item {number} is \"other\": add \"| LABEL | FIX LABEL\" (1-3 words each).");
                        continue;
                    }

                    parsed.Add(new StoryboardItem("other", line, fix, parts[3].ToUpperInvariant(), parts[4].ToUpperInvariant()));
                }
                else
                {
                    problems.Add($"Item {number}: '{parts[0]}' is not a known mistake id (use one from the list, or \"other\").");
                }
            }

            if (parsed.Count is < 2 or > 4)
            {
                problems.Add($"A mistakes video needs 2 to 4 numbered items (got {parsed.Count}).");
            }

            var payoffParts = (fields.GetValueOrDefault("PAYOFF") ?? string.Empty).Split('|').Select(p => p.Trim()).ToArray();
            var payoff = Sentence(payoffParts[0], "PAYOFF", problems);
            var payoffWord = payoffParts.Length > 1 && Words(payoffParts[1]) is >= 1 and <= 3 ? payoffParts[1] : null;
            return problems.Count > 0
                ? (null, problems)
                : (new Storyboard(StoryboardFormat.Countdown, hook, intro, parsed, payoff, payoffWord, cta), []);
        }

        if (format is "levels" or "level")
        {
            var ladderId = fields.GetValueOrDefault("LADDER")?.ToLowerInvariant();
            var ladder = ladderId is null ? LevelLadders.Pick(topic) : LevelLadders.Find(ladderId);
            if (ladder is null)
            {
                problems.Add($"LADDER '{ladderId}' is unknown (use one of: {string.Join(", ", LevelLadders.All.Select(l => l.Id))}).");
                return (null, problems);
            }

            var lines = items.Values.Select((v, i) => Sentence(v, $"level {i + 1}", problems)).ToList();
            if (lines.Count < 2 || lines.Count > ladder.Steps.Count)
            {
                problems.Add($"The '{ladder.Id}' ladder has {ladder.Steps.Count} levels; give 2 to {ladder.Steps.Count} numbered lines (got {lines.Count}).");
            }

            var levelItems = ladder.Steps.Zip(lines, (step, line) => new StoryboardItem(step.Subject, line)).ToList();
            return problems.Count > 0
                ? (null, problems)
                : (new Storyboard(StoryboardFormat.Levels, hook, intro, levelItems, null, null, cta, ladder.Id), []);
        }

        problems.Insert(0, $"FORMAT must be \"mistakes\" or \"levels\" (got '{fields.GetValueOrDefault("FORMAT")}').");
        return (null, problems);
    }

    /// <summary>
    /// Parses, composes and checks the length - what both job creation and the pipeline need.
    /// </summary>
    public static (Storyboard? Storyboard, ComposedStoryboard? Composed, IReadOnlyList<string> Problems) Build(
        string text, string topic, IReadOnlyList<ArtSubject> subjects, IReadOnlyCollection<string> catalogIds)
    {
        var (storyboard, problems) = Parse(text, topic, subjects);
        if (storyboard is null)
        {
            return (null, null, problems);
        }

        var composed = StoryboardComposer.Compose(storyboard, catalogIds, subjects.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase));
        var lengthProblems = ScriptValidator.Validate(composed.Narration).Reasons
            .Select(r => r.StartsWith("Too", StringComparison.Ordinal) ? $"{r} (counted with the \"Number one:\" / \"Level one:\" words the video adds)" : r)
            .ToList();
        return lengthProblems.Count > 0 ? (null, null, lengthProblems) : (storyboard, composed, []);
    }

    // --- the ChatGPT prompt ------------------------------------------------------------------

    /// <summary>The prompt the owner gives ChatGPT once: channel rules, the formats, and the current lists.</summary>
    public static string ChatGptPrompt(IReadOnlyList<ArtSubject> subjects)
    {
        var mistakes = subjects.Where(s => s.Kind == ArtKind.Mistake).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("You are the scriptwriter for my faceless fitness YouTube Shorts channel. The videos star an original anime-style");
        sb.AppendLine("mascot, are 25-40 seconds long, and are narrated by a calm male English voice. My app turns your answer into the");
        sb.AppendLine("video automatically, so follow the rules and formats below exactly. I may describe the video in Turkish; you");
        sb.AppendLine("always write English.");
        sb.AppendLine();
        sb.AppendLine("PICK THE FORMAT");
        sb.AppendLine("- Mistakes or tips about how to do an exercise from the mistake list below -> MISTAKES format.");
        sb.AppendLine("- Levels, ranking, \"which one are you\" for one of the ladders below -> LEVELS format.");
        sb.AppendLine("- Anything else (fat loss, nutrition, sleep, myths, what happens in the body, motivation) -> PLAIN script.");
        sb.AppendLine();
        sb.AppendLine("RULES FOR EVERY FORMAT");
        sb.AppendLine("- The whole spoken text is 85 to 120 words (the app adds \"Number one:\" / \"Level one: The plank.\" itself and");
        sb.AppendLine("  counts those too). Never more than 135.");
        sb.AppendLine("- Short, complete sentences, one idea each. Plain spoken English: no emoji, no hashtags, no quotation marks.");
        sb.AppendLine("- HOOK, the first sentence: at most 12 words, speaks to the viewer as \"you\" - a pointed question, a surprising");
        sb.AppendLine("  claim, or the mistake they are making right now. Never start with: When it comes to, If you're, In this video,");
        sb.AppendLine("  Today, Hey, Welcome, Let's, Many people, It's no secret, or a definition.");
        sb.AppendLine("- Open a question early and answer it near the end; use moments the viewer recognizes (the last rep, the");
        sb.AppendLine("  number on the bar), not textbook words. Numbered points count UP from one, the most important last.");
        sb.AppendLine("- Exercise technique, training, recovery and general nutrition only. Never diagnose, prescribe, promise a cure,");
        sb.AppendLine("  claim to treat an injury, or guarantee results; cautious wording for anything health-related. Never invent");
        sb.AppendLine("  statistics or studies. Never name real people, channels, brands or products.");
        sb.AppendLine("- Output ONLY the script in the chosen format, nothing before or after it, no code block.");
        sb.AppendLine();
        sb.AppendLine("MISTAKES FORMAT (2 to 4 mistakes; 3 is best):");
        sb.AppendLine("FORMAT: mistakes");
        sb.AppendLine("HOOK: <hook sentence>");
        sb.AppendLine("INTRO: <one sentence promising the mistakes and teasing that the last one surprises most people>");
        sb.AppendLine("1: <mistake id> | <what they do wrong and what it costs, max 14 words> | <exactly what to do instead, max 14 words>");
        sb.AppendLine("2: ...");
        sb.AppendLine("3: ...");
        sb.AppendLine("PAYOFF: <one sentence on what they get once it is fixed> | <1-2 words for the screen>");
        sb.AppendLine("CTA: <short question: which one were you doing?>");
        sb.AppendLine("- Do not write \"Number one\" in the lines - the app adds it. Each mistake is shown as a drawing of the wrong form");
        sb.AppendLine("  next to the right form, so pick ids from this list (copy exactly):");
        foreach (var group in mistakes.GroupBy(m => m.Exercise))
        {
            sb.AppendLine($"  {group.Key}: {string.Join(", ", group.Select(m => $"{m.Id} ({m.Label!.ToLowerInvariant()} -> {m.FixLabel!.ToLowerInvariant()})"))}");
        }

        sb.AppendLine("- Only when no listed id fits, use: <n>: other | <problem> | <fix> | <LABEL, 1-3 words> | <FIX LABEL, 1-3 words>");
        sb.AppendLine();
        sb.AppendLine("LEVELS FORMAT (easy to hard):");
        sb.AppendLine("FORMAT: levels");
        sb.AppendLine("LADDER: <ladder id>");
        sb.AppendLine("HOOK: <hook question about their level>");
        sb.AppendLine("INTRO: <one sentence: most people get stuck at one of these levels>");
        sb.AppendLine("1: <one sentence, max 14 words, that challenges them with that level's benchmark>");
        sb.AppendLine("2: ...");
        sb.AppendLine("CTA: <short question: which level are you at?>");
        sb.AppendLine("- Do not name the level or the exercise in the lines - the app says \"Level one: The plank.\" first. Keep the");
        sb.AppendLine("  benchmark's numbers exactly. One line per level of the ladder:");
        foreach (var ladder in LevelLadders.All)
        {
            sb.AppendLine($"  {ladder.Id}: {string.Join(" -> ", ladder.Steps.Select((s, i) => $"{i + 1} {s.Name.Replace("the ", string.Empty, StringComparison.Ordinal)} ({s.Benchmark})"))}");
        }

        sb.AppendLine();
        sb.AppendLine("PLAIN FORMAT: just the narration text, 85-120 words, told as a story (a numbered list counting up, a myth that");
        sb.AppendLine("falls apart, one guy who was stuck and changed one thing, or what happens in the body over seconds, hours, days).");
        sb.AppendLine("The last sentence ties back to the hook.");
        sb.AppendLine();
        sb.AppendLine("Before answering, silently check the word count, the hook and the format. Reply \"Ready\" now; from my next");
        sb.AppendLine("message on, write one script per request.");
        return sb.ToString();
    }

    // --- helpers -----------------------------------------------------------------------------

    private static string Field(Dictionary<string, string> fields, string key, int maxWords, List<string> problems)
    {
        var value = fields.GetValueOrDefault(key);
        if (value is null)
        {
            problems.Add($"The {key}: line is missing.");
            return string.Empty;
        }

        var words = Words(value);
        if (words < 3 || words > maxWords)
        {
            problems.Add($"{key} has {words} words; it needs 3 to {maxWords}.");
        }

        return value;
    }

    private static string Sentence(string value, string what, List<string> problems)
    {
        var words = Words(value);
        if (words < 3 || words > MaxLineWords)
        {
            problems.Add($"The {what} has {words} words; it needs 3 to {MaxLineWords}.");
        }

        return value;
    }

    private static int Words(string? text) => string.IsNullOrWhiteSpace(text) ? 0 : Spaces().Split(text.Trim()).Length;

    [GeneratedRegex(@"^\s*[*_`>\- ]*FORMAT\s*[*_]*\s*:", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex FormatLine();

    [GeneratedRegex(@"^\s*[*_`>\- ]*(?<key>FORMAT|LADDER|HOOK|INTRO|PAYOFF|CTA|\d+)\s*[*_]*\s*[:.)]\s*(?<value>.+?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex KeyLine();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
