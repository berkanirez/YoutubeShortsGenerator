using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Application;

// The visual-first path (docs/VIDEO_QUALITY_RESEARCH.md): instead of free narration that is
// split and decorated afterwards, the LLM fills a storyboard - a format defined in code, with
// its choices limited to controlled lists (mistake ids from subjects.json, a level ladder) -
// and StoryboardComposer turns it into scenes whose shots show the point without sound. The
// script and the plan come out together. When the topic doesn't suit a storyboard format, or
// the model can't produce a valid storyboard, the classic script -> plan path runs instead.
public sealed partial class StoryboardService
{
    private const int MaxAttempts = 3;
    private const int MaxHookWords = 12;
    private const int MaxLineWords = 20;

    private readonly ILlmProvider _llm;
    private readonly IVideoJobRepository _repository;
    private readonly IVideoPlanStore _planStore;
    private readonly IAssetRepository _assets;
    private readonly IArtSubjectCatalog _subjects;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<StoryboardService> _logger;

    public StoryboardService(
        ILlmProvider llm,
        IVideoJobRepository repository,
        IVideoPlanStore planStore,
        IAssetRepository assets,
        IArtSubjectCatalog subjects,
        TimeProvider timeProvider,
        ILogger<StoryboardService>? logger = null)
    {
        _llm = llm;
        _repository = repository;
        _planStore = planStore;
        _assets = assets;
        _subjects = subjects;
        _timeProvider = timeProvider;
        _logger = logger ?? NullLogger<StoryboardService>.Instance;
    }

    // Topics about the body or lifestyle, not about how to do an exercise: a countdown of
    // technique mistakes would answer a different question ("Do This and Get Shredded" got
    // squat and deadlift form fixes). They take the classic, story-shaped path.
    private static readonly string[] NotTechniqueWords =
    [
        "shred", "shredded", "lean", "fat", "belly", "cut", "cutting", "bulk", "bulking", "diet", "calorie", "calories",
        "deficit", "protein", "eat", "eating", "food", "meal", "meals", "nutrition", "sleep", "recovery", "rest", "testosterone",
        "hormone", "hormones", "creatine", "supplement", "supplements", "cardio", "steps", "weight loss", "lose weight",
        "motivation", "discipline", "stress", "water", "caffeine", "coffee", "body fat", "recomp", "aesthetic",
    ];

    // Words that make a "mistakes" topic about technique even without naming an exercise.
    private static readonly string[] TechniqueWords =
        ["form", "technique", "gym", "lifting", "lift", "lifts", "workout", "training", "exercise", "exercises", "rep", "reps"];

    /// <summary>
    /// The storyboard format for a topic, or null when it should go the classic way.
    /// - Levels: the topic asks for levels/ranking (a ladder of exercises).
    /// - Countdown of mistakes: the topic names an exercise the mistake list covers, or asks for
    ///   "mistakes" in a technique context ("3 Gym Mistakes").
    /// - Everything else - fat loss, nutrition, recovery, myths, "what happens" - is classic.
    /// A general exercise topic after two countdowns becomes levels when it names a ladder.
    /// </summary>
    public static StoryboardFormat? ChooseFormat(string topic, IReadOnlyList<string> recentScripts, IReadOnlyList<ArtSubject> subjects)
    {
        var text = Normalize(topic);
        bool Has(string phrase) => text.Contains($" {Normalize(phrase).Trim()} ", StringComparison.Ordinal);
        var asked = StoryFormats.Explicit(topic);
        if (asked == StoryFormat.Levels)
        {
            return StoryboardFormat.Levels;
        }

        if (asked is StoryFormat.MythStory or StoryFormat.InsideTheBody || NotTechniqueWords.Any(Has))
        {
            return null;
        }

        var exercisesWithMistakes = subjects
            .Where(s => s.Kind == ArtKind.Mistake && s.Exercise is not null)
            .Select(s => s.Exercise!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var namesExercise = subjects
            .Where(s => exercisesWithMistakes.Contains(s.Id))
            .Any(s => s.Terms.Any(Has));
        if (asked == StoryFormat.Countdown)
        {
            return namesExercise || TechniqueWords.Any(Has) ? StoryboardFormat.Countdown : null;
        }

        if (!namesExercise)
        {
            return null;
        }

        var namesLadder = LevelLadders.Pick(topic).Keywords.Any(Has);
        var lastTwoCountdowns = recentScripts.Take(2).Count(s => StoryFormats.Detect(s) == StoryFormat.Countdown) == 2;
        return namesLadder && lastTwoCountdowns ? StoryboardFormat.Levels : StoryboardFormat.Countdown;
    }

    /// <summary>
    /// Writes the job's script and plan from a storyboard. False when the topic should take the
    /// classic path or no valid storyboard came out (the caller then runs the classic script).
    /// </summary>
    public async Task<bool> TryGenerateAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await _repository.GetByIdAsync(jobId, cancellationToken)
                  ?? throw new KeyNotFoundException($"Video job '{jobId}' was not found.");
        if (job.GeneratedScript is not null || job.Status is not (VideoJobStatus.Created or VideoJobStatus.Planning))
        {
            return false;
        }

        // An owner-written storyboard (StoryboardScript) is used as written - no LLM.
        if (StoryboardScript.IsStructured(job.SourceScript))
        {
            var subjectsForScript = await _subjects.GetAllAsync(cancellationToken);
            var catalogForScript = (await _assets.GetAllAsync(cancellationToken)).Select(a => a.Id).ToList();
            var (ownStoryboard, ownComposed, ownProblems) = StoryboardScript.Build(job.SourceScript!, job.Topic, subjectsForScript, catalogForScript);
            if (ownStoryboard is null || ownComposed is null)
            {
                var message = $"Your storyboard script can't be used: {string.Join(" ", ownProblems)}";
                job.MarkFailed(message, _timeProvider.GetUtcNow());
                await _repository.UpdateAsync(job, cancellationToken);
                throw new ScriptGenerationException(message);
            }

            await SaveAsync(job, ownStoryboard, ownComposed, cancellationToken);
            _logger.LogInformation("Job {JobId}: owner's {Format} storyboard used ({Scenes} scenes)", jobId, ownStoryboard.Format, ownComposed.Scenes.Count);
            return true;
        }

        if (job.SourceScript is not null)
        {
            return false;
        }

        var recent = (await _repository.ListRecentAsync(10, cancellationToken))
            .Where(j => j.Id != jobId && !string.IsNullOrWhiteSpace(j.GeneratedScript))
            .Select(j => j.GeneratedScript!)
            .ToList();
        var subjectList = await _subjects.GetAllAsync(cancellationToken);
        var format = ChooseFormat(job.Topic, recent, subjectList);
        if (format is null)
        {
            return false;
        }

        var catalog = (await _assets.GetAllAsync(cancellationToken)).Select(a => a.Id).ToList();
        var subjects = subjectList.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
        var request = format == StoryboardFormat.Levels
            ? LevelsRequest(job.Topic)
            : CountdownRequest(job.Topic, subjects.Values.ToList(), catalog);
        if (request is null)
        {
            return false;
        }

        IReadOnlyList<string> reasons = [];
        (Storyboard Storyboard, ComposedStoryboard Composed)? withWeakHook = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            var prompt = request.Prompt + Retry(reasons);
            var raw = await _llm.GenerateJsonAsync(prompt, request.Schema, cancellationToken);
            var (storyboard, problems) = request.Parse(raw);
            if (storyboard is null)
            {
                reasons = problems;
                _logger.LogInformation("Job {JobId}: storyboard attempt {Attempt} rejected: {Reasons}", jobId, attempt, string.Join("; ", problems));
                continue;
            }

            var composed = StoryboardComposer.Compose(storyboard, catalog, subjects);
            var checks = ScriptValidator.Validate(composed.Narration).Reasons.ToList();
            var plan = BuildPlan(job.Topic, storyboard, composed);
            checks.AddRange(VideoPlanValidator.Validate(plan, composed.Narration, catalog.ToHashSet(StringComparer.OrdinalIgnoreCase)).Reasons);
            if (checks.Count > 0)
            {
                reasons = checks;
                _logger.LogInformation("Job {JobId}: storyboard attempt {Attempt} rejected: {Reasons}", jobId, attempt, string.Join("; ", checks));
                continue;
            }

            var hookIssues = HookRule.Check(composed.Narration);
            if (hookIssues.Count > 0 && attempt < MaxAttempts)
            {
                withWeakHook ??= (storyboard, composed);
                reasons = hookIssues;
                continue;
            }

            await SaveAsync(job, storyboard, composed, cancellationToken);
            _logger.LogInformation("Job {JobId}: {Format} storyboard accepted on attempt {Attempt} ({Scenes} scenes)",
                jobId, format, attempt, composed.Scenes.Count);
            return true;
        }

        if (withWeakHook is { } fallback)
        {
            await SaveAsync(job, fallback.Storyboard, fallback.Composed, cancellationToken);
            return true;
        }

        _logger.LogWarning("Job {JobId}: no valid {Format} storyboard after {Attempts} attempts ({Reasons}); using the classic script",
            jobId, format, MaxAttempts, string.Join("; ", reasons));
        return false;
    }

    private async Task SaveAsync(VideoJob job, Storyboard storyboard, ComposedStoryboard composed, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        if (job.Status == VideoJobStatus.Created)
        {
            job.MarkPlanning(now);
        }

        job.AttachGeneratedScript(composed.Narration, now);
        var directory = await _planStore.SaveAsync(job.Id, BuildPlan(job.Topic, storyboard, composed), cancellationToken);
        job.SetOutputDirectory(directory, now);
        await _repository.UpdateAsync(job, cancellationToken);
    }

    public static VideoPlan BuildPlan(string topic, Storyboard storyboard, ComposedStoryboard composed) => new(
        topic,
        storyboard.Hook,
        Math.Round(composed.Scenes.Sum(s => s.DurationSeconds), 1),
        composed.Narration,
        composed.Scenes,
        Style: null,
        CaptionStyle: null,
        MusicMood: "upbeat",
        Takeaway: storyboard.Payoff,
        Storyboard: storyboard,
        WantedArt: composed.WantedArt);

    private sealed record Request(string Prompt, string Schema, Func<string, (Storyboard?, IReadOnlyList<string>)> Parse);

    // --- Countdown -----------------------------------------------------------------------

    /// <summary>The countdown request, or null when the named exercise has too few mistakes for one.</summary>
    private static Request? CountdownRequest(string topic, IReadOnlyList<ArtSubject> subjects, IReadOnlyCollection<string> catalog)
    {
        var mistakes = subjects.Where(s => s.Kind == ArtKind.Mistake).ToList();
        // A topic that names an exercise counts down only that exercise's mistakes - a deadlift
        // video once got a push-up and a squat mistake to make up three. Two is a fine countdown.
        var named = subjects
            .Where(s => s.Kind is ArtKind.ExercisePair or ArtKind.Exercise && mistakes.Any(m => m.Exercise == s.Id))
            .Where(s => s.Terms.Any(t => Normalize(topic).Contains($" {Normalize(t).Trim()} ", StringComparison.Ordinal)))
            .Select(s => s.Id)
            .ToHashSet();
        var options = named.Count > 0 ? mistakes.Where(m => named.Contains(m.Exercise!)).ToList() : mistakes;
        var count = Math.Min(3, options.Count);
        if (count < 2)
        {
            return null;
        }

        // Mistakes whose wrong-form picture exists come first and are marked: they make the
        // wrong-vs-right split, the strongest shot.
        var drawn = new HashSet<string>(catalog, StringComparer.OrdinalIgnoreCase);
        options = options.OrderByDescending(m => drawn.Contains(m.Id)).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("You are writing a 25-40 second vertical fitness Short in the \"countdown of mistakes\" format.");
        sb.AppendLine($"Topic: {topic}");
        sb.AppendLine();
        sb.AppendLine($"Pick exactly {count} different mistakes from this list (copy the id exactly). Order them from the least to the");
        sb.AppendLine("most important: the video numbers them 1, 2, 3 and the last one is the big one. Prefer the ones marked");
        sb.AppendLine("(illustrated) when they fit the topic.");
        foreach (var m in options)
        {
            sb.AppendLine($"- {m.Id}: {m.Label!.ToLowerInvariant()} ({m.Exercise}); the fix: {m.FixLabel!.ToLowerInvariant()}{(drawn.Contains(m.Id) ? " (illustrated)" : string.Empty)}");
        }

        sb.AppendLine();
        sb.AppendLine("Fields:");
        sb.AppendLine($"- hook: the very first sentence, at most {MaxHookWords - 2} words. Speak to the viewer as \"you\": a pointed question");
        sb.AppendLine("  or a bold claim about the mistake they are making. No greeting, no \"In this video\", no \"When it comes to\".");
        sb.AppendLine($"- intro: one short sentence that promises {count} mistakes and teases that the last one surprises most people.");
        sb.AppendLine("  Never start it with \"In this video\" or \"We'll show you\" - talk straight to the viewer.");
        sb.AppendLine("- mistakes: for each one, \"problem\" = one sentence (at most 14 words) on what the viewer does wrong and what");
        sb.AppendLine("  it costs them, and \"fix\" = one sentence (at most 14 words) telling exactly what to do instead.");
        sb.AppendLine("  Do not write \"number one\" yourself - the video adds it.");
        sb.AppendLine("- payoff: one sentence on what they get once all three are fixed.");
        sb.AppendLine("- payoffWord: one or two words for the screen naming that result.");
        sb.AppendLine("- cta: a short question asking which of the mistakes they were making.");
        sb.AppendLine("Plain spoken English. No medical claims, cautious wording, never name real people, channels or brands.");

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["hook"] = Str(),
                ["intro"] = Str(),
                ["mistakes"] = new JsonObject
                {
                    ["type"] = "array",
                    ["minItems"] = count,
                    ["maxItems"] = count,
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject
                        {
                            ["mistake"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray(options.Select(m => (JsonNode?)m.Id).ToArray()) },
                            ["problem"] = Str(),
                            ["fix"] = Str(),
                        },
                        ["required"] = new JsonArray("mistake", "problem", "fix"),
                    },
                },
                ["payoff"] = Str(),
                ["payoffWord"] = Str(),
                ["cta"] = Str(),
            },
            ["required"] = new JsonArray("hook", "intro", "mistakes", "payoff", "payoffWord", "cta"),
        }.ToJsonString();

        var allowed = options.Select(m => m.Id).ToHashSet();
        return new Request(sb.ToString(), schema, raw => ParseCountdown(raw, allowed, count));
    }

    public static (Storyboard?, IReadOnlyList<string>) ParseCountdown(string raw, IReadOnlySet<string> allowedMistakes, int count = 3)
    {
        var problems = new List<string>();
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(PlanJsonExtractor.Extract(raw));
        }
        catch (JsonException ex)
        {
            return (null, [$"The answer was not valid JSON ({ex.Message})."]);
        }

        var hook = Text(root, "hook", problems, MaxHookWords);
        var intro = Text(root, "intro", problems, MaxLineWords);
        var payoff = Text(root, "payoff", problems, MaxLineWords);
        var cta = Text(root, "cta", problems, MaxLineWords);
        var payoffWord = root?["payoffWord"]?.GetValue<string>();
        var items = new List<StoryboardItem>();
        foreach (var node in root?["mistakes"]?.AsArray() ?? [])
        {
            var id = node?["mistake"]?.GetValue<string>() ?? string.Empty;
            if (!allowedMistakes.Contains(id))
            {
                problems.Add($"'{id}' is not one of the listed mistake ids.");
                continue;
            }

            items.Add(new StoryboardItem(id, Text(node, "problem", problems, MaxLineWords), Text(node, "fix", problems, MaxLineWords)));
        }

        if (items.Count != count)
        {
            problems.Add($"Give exactly {count} mistakes (got {items.Count}).");
        }
        else if (items.Select(i => i.Subject).Distinct().Count() != count)
        {
            problems.Add($"The {count} mistakes must all be different.");
        }

        return problems.Count > 0
            ? (null, problems)
            : (new Storyboard(StoryboardFormat.Countdown, hook, intro, items, payoff, payoffWord, cta), []);
    }

    // --- Levels --------------------------------------------------------------------------

    private static Request LevelsRequest(string topic)
    {
        var ladder = LevelLadders.Pick(topic);
        var sb = new StringBuilder();
        sb.AppendLine("You are writing a 25-35 second vertical fitness Short in the \"levels\" format: a ladder of exercises from");
        sb.AppendLine("easy to hard, and the viewer wonders which level they are.");
        sb.AppendLine($"Topic: {topic}");
        sb.AppendLine("The ladder is fixed, easiest first:");
        for (var i = 0; i < ladder.Steps.Count; i++)
        {
            sb.AppendLine($"- Level {i + 1}: {ladder.Steps[i].Name} - benchmark: {ladder.Steps[i].Benchmark}");
        }

        sb.AppendLine();
        sb.AppendLine("Fields:");
        sb.AppendLine($"- hook: the very first sentence, at most {MaxHookWords - 2} words, a question to the viewer about their level.");
        sb.AppendLine("- intro: one short sentence teasing that most people get stuck at one of these levels.");
        sb.AppendLine($"- levels: exactly {ladder.Steps.Count} items in the same order; each \"line\" is one sentence (at most 14 words) that");
        sb.AppendLine("  challenges the viewer with that level's benchmark - keep its numbers exactly. Do not name the level or the");
        sb.AppendLine("  exercise - the video says it. Do not invent anything the benchmark doesn't say.");
        sb.AppendLine("- cta: a short question asking which level they are at.");
        sb.AppendLine("Plain spoken English. No medical claims, never name real people, channels or brands.");

        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["hook"] = Str(),
                ["intro"] = Str(),
                ["levels"] = new JsonObject
                {
                    ["type"] = "array",
                    ["minItems"] = ladder.Steps.Count,
                    ["maxItems"] = ladder.Steps.Count,
                    ["items"] = new JsonObject
                    {
                        ["type"] = "object",
                        ["properties"] = new JsonObject { ["line"] = Str() },
                        ["required"] = new JsonArray("line"),
                    },
                },
                ["cta"] = Str(),
            },
            ["required"] = new JsonArray("hook", "intro", "levels", "cta"),
        }.ToJsonString();

        return new Request(sb.ToString(), schema, raw => ParseLevels(raw, ladder));
    }

    public static (Storyboard?, IReadOnlyList<string>) ParseLevels(string raw, LevelLadder ladder)
    {
        var problems = new List<string>();
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(PlanJsonExtractor.Extract(raw));
        }
        catch (JsonException ex)
        {
            return (null, [$"The answer was not valid JSON ({ex.Message})."]);
        }

        var hook = Text(root, "hook", problems, MaxHookWords);
        var intro = Text(root, "intro", problems, MaxLineWords);
        var cta = Text(root, "cta", problems, MaxLineWords);
        var lines = (root?["levels"]?.AsArray() ?? []).Select(n => Text(n, "line", problems, MaxLineWords)).ToList();
        if (lines.Count != ladder.Steps.Count)
        {
            problems.Add($"Give exactly {ladder.Steps.Count} levels (got {lines.Count}).");
        }

        var items = ladder.Steps.Zip(lines, (step, line) => new StoryboardItem(step.Subject, line)).ToList();
        return problems.Count > 0
            ? (null, problems)
            : (new Storyboard(StoryboardFormat.Levels, hook, intro, items, null, null, cta, ladder.Id), []);
    }

    // --- helpers -------------------------------------------------------------------------

    private static JsonObject Str() => new() { ["type"] = "string" };

    private static string Text(JsonNode? node, string field, List<string> problems, int maxWords)
    {
        string? value = null;
        try
        {
            value = node?[field]?.GetValue<string>();
        }
        catch (InvalidOperationException)
        {
        }

        var text = Spaces().Replace(value ?? string.Empty, " ").Trim();
        var words = text.Length == 0 ? 0 : text.Split(' ').Length;
        if (words < 3)
        {
            problems.Add($"\"{field}\" is missing or too short.");
        }
        else if (words > maxWords)
        {
            problems.Add($"\"{field}\" has {words} words; keep it to at most {maxWords}.");
        }

        return text;
    }

    private static string Retry(IReadOnlyList<string> reasons) =>
        reasons.Count == 0
            ? string.Empty
            : "\nYour previous answer was rejected - fix all of these:\n" + string.Join("\n", reasons.Select(r => $"- {r}"));

    private static string Normalize(string text) => " " + NonWord().Replace(text.ToLowerInvariant(), " ") + " ";

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonWord();
}
