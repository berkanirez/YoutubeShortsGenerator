using System.Text.RegularExpressions;

namespace ShortsGenerator.Domain;

/// <param name="Scenes">One scene per beat, in order.</param>
/// <param name="WantedArt">Mistake pictures the video would show if they existed (the art gate asks for them).</param>
/// <param name="Narration">The scenes' narrations joined: the job's script.</param>
public sealed record ComposedStoryboard(IReadOnlyList<Scene> Scenes, IReadOnlyList<string> WantedArt, string Narration);

// Turns a storyboard into scenes - deterministically, so it can run again when new art
// arrives and give the same narration with better pictures. Every scene gets a shot that
// carries a visual sign (cross/check, arrow, pain point, number, meter): the "mute test"
// holds by construction. Mascot poses and the hook shot are left for VisualDirector, which
// picks them against the last videos.
public static partial class StoryboardComposer
{
    // Kokoro's pace (~3 words/s measured with pauses); replaced by real timing after narration.
    private const double WordsPerSecond = 2.8;

    private static readonly string[] CountWords = ["zero", "one", "two", "three", "four", "five"];

    public static ComposedStoryboard Compose(
        Storyboard storyboard, IReadOnlyCollection<string> catalogIds, IReadOnlyDictionary<string, ArtSubject> subjects)
    {
        var catalog = new HashSet<string>(catalogIds, StringComparer.OrdinalIgnoreCase);
        var beats = storyboard.Format == StoryboardFormat.Levels
            ? LevelBeats(storyboard, catalog, subjects)
            : CountdownBeats(storyboard, catalog, subjects, out _);
        var wanted = storyboard.Format == StoryboardFormat.Countdown
            ? storyboard.Items
                .Select(i => i.Subject)
                .Where(id => subjects.TryGetValue(id, out var s) && s.Kind == ArtKind.Mistake && !catalog.Contains(id))
                .Distinct()
                .ToList()
            : [];

        var scenes = beats.Select((b, i) => b with
        {
            Emphasis = b.Emphasis ?? SpokenPhrase(b.Narration, b.OnScreenText) ?? SpokenPhrase(b.Narration, b.SecondaryText)
                ?? LoosePhrase(b.Narration, b.OnScreenText) ?? LoosePhrase(b.Narration, b.SecondaryText),
            Id = $"s{i + 1}",
            Order = i + 1,
            DurationSeconds = Math.Clamp(Math.Round(Words(b.Narration) / WordsPerSecond, 1), 1.5, 12),
        }).ToList();
        return new ComposedStoryboard(scenes, wanted, string.Join(' ', scenes.Select(s => s.Narration)));
    }

    private static List<Scene> CountdownBeats(
        Storyboard sb, HashSet<string> catalog, IReadOnlyDictionary<string, ArtSubject> subjects, out int count)
    {
        count = sb.Items.Count;
        var first = Mistake(sb.Items[0], subjects);
        var firstWrong = first is not null && catalog.Contains(first.Id) ? first.Id : null;
        var firstRight = first is null ? null : RightPose(first, subjects);
        var beats = new List<Scene>
        {
            Beat(SceneType.Hook, sb.Hook, ShotType.HookStop, sfx: SfxId.Impact) with
            {
                Count = count,
                AltAssetIds = firstWrong is not null && firstRight is not null ? [firstWrong, firstRight.Value.Asset] : null,
                OnScreenText = "STOP",
            },
            Beat(SceneType.List, sb.Intro, ShotType.KineticWord, sfx: SfxId.Whoosh) with
            {
                OnScreenText = $"{count} MISTAKES",
                Count = count,
            },
        };

        for (var i = 0; i < sb.Items.Count; i++)
        {
            var item = sb.Items[i];
            var number = i + 1;
            var mistake = Mistake(item, subjects);
            var right = mistake is null ? null : RightPose(mistake, subjects);
            var hasWrong = mistake is not null && catalog.Contains(mistake.Id);
            var problem = $"Number {CountWords[Math.Clamp(number, 0, CountWords.Length - 1)]}: {Sentence(item.Line)}";

            beats.Add(hasWrong && right is not null
                ? Beat(SceneType.Comparison, problem, ShotType.SplitFix, sfx: SfxId.Error) with
                {
                    AssetIds = [right.Value.Asset],
                    AltAssetIds = [mistake!.Id],
                    OnScreenText = mistake.Label,
                    SecondaryText = mistake.FixLabel,
                    FocusPoint = mistake.Point,
                    Arrow = mistake.Arrow,
                    Count = number,
                    Total = count,
                }
                : Beat(SceneType.MythVsFact, problem, ShotType.MistakeReaction, sfx: SfxId.Error) with
                {
                    OnScreenText = mistake?.Label ?? item.Label,
                    Count = number,
                    Total = count,
                });

            // A mistake with no exercise picture (an "other" item from an owner's script): the fix
            // is painted big behind the mascot instead of an empty demo.
            if (right is null)
            {
                beats.Add(Beat(SceneType.ExerciseDemo, Sentence(item.Fix ?? string.Empty), ShotType.KineticWord, sfx: SfxId.Success) with
                {
                    OnScreenText = mistake?.FixLabel ?? item.FixLabel,
                    Count = number,
                    Total = count,
                });
                continue;
            }

            // Alternate the two ways of showing the fix so the three items don't look alike.
            var fixShot = i % 2 == 0 ? ShotType.FixDemo : ShotType.ZoomDetail;
            beats.Add(Beat(SceneType.ExerciseDemo, Sentence(item.Fix ?? string.Empty), fixShot, sfx: SfxId.Success) with
            {
                AssetIds = [right.Value.Asset],
                AltAssetIds = right.Value.Partner is { } partner ? [partner] : null,
                OnScreenText = mistake?.FixLabel,
                FocusPoint = mistake?.Point,
                Arrow = mistake?.Arrow,
                Count = number,
                Total = count,
            });
        }

        beats.Add(Beat(SceneType.Progress, Sentence(sb.Payoff ?? string.Empty), ShotType.Payoff, sfx: SfxId.Success) with
        {
            OnScreenText = Short(sb.PayoffWord, 3),
        });
        beats.Add(Beat(SceneType.Outro, Sentence(sb.Cta), ShotType.CtaLoop, sfx: SfxId.Pop) with
        {
            OnScreenText = "WHICH ONE?",
        });
        return beats;
    }

    private static List<Scene> LevelBeats(Storyboard sb, HashSet<string> catalog, IReadOnlyDictionary<string, ArtSubject> subjects)
    {
        var ladder = LevelLadders.Find(sb.Ladder) ?? LevelLadders.All[0];
        var steps = ladder.Steps.Take(sb.Items.Count).ToList();
        var lineup = steps.Select(s => FirstAsset(s.Subject, subjects)).Where(id => id is not null && catalog.Contains(id)).Cast<string>().ToList();
        var total = steps.Count;
        var beats = new List<Scene>
        {
            Beat(SceneType.Hook, sb.Hook, ShotType.Lineup, sfx: SfxId.Impact) with
            {
                AssetIds = lineup,
                OnScreenText = "WHICH ONE ARE YOU?",
                Total = total,
            },
            Beat(SceneType.List, sb.Intro, ShotType.KineticWord, sfx: SfxId.Whoosh) with
            {
                OnScreenText = $"{total} LEVELS",
                Count = total,
            },
        };

        for (var i = 0; i < steps.Count; i++)
        {
            var asset = FirstAsset(steps[i].Subject, subjects);
            var partner = subjects.TryGetValue(steps[i].Subject, out var s) && s.Kind == ArtKind.ExercisePair ? s.AssetIds[1] : null;
            var said = Sentence(sb.Items[i].Line);
            var line = $"Level {CountWords[i + 1]}: {Capitalize(steps[i].Name)}. {said}";
            var (goal, unit) = Goal(steps[i].Benchmark);
            beats.Add(Beat(SceneType.Progress, line, ShotType.LevelShow, sfx: SfxId.LevelUp) with
            {
                // The benchmark is what to stress ("for 60 seconds"); the name is already said on its own.
                Emphasis = BenchmarkPhrase(said),
                GoalValue = goal,
                GoalUnit = unit,
                AssetIds = asset is not null && catalog.Contains(asset) ? [asset] : [],
                AltAssetIds = partner is not null && catalog.Contains(partner) ? [partner] : null,
                OnScreenText = $"LEVEL {i + 1}",
                SecondaryText = Capitalize(steps[i].Name.Replace("the ", string.Empty, StringComparison.Ordinal)),
                Count = i + 1,
                Total = total,
            });
        }

        beats.Add(Beat(SceneType.Outro, Sentence(sb.Cta), ShotType.Lineup, sfx: SfxId.Pop) with
        {
            AssetIds = lineup,
            OnScreenText = "COMMENT YOUR LEVEL",
            Total = total,
        });
        return beats;
    }

    // "a straight-line plank held for 60 seconds" -> (60, seconds); "8 strict pull-ups" -> (8, reps);
    // "one clean muscle-up" -> (1, reps); no number -> none.
    public static (int? Value, string? Unit) Goal(string benchmark)
    {
        var b = benchmark.ToLowerInvariant();
        var seconds = Regex.Match(b, @"(\d+)\s*seconds?");
        if (seconds.Success)
        {
            return (int.Parse(seconds.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), "seconds");
        }

        var number = Regex.Match(b, @"\b(\d+|one|two|three|four|five)\b");
        if (!number.Success)
        {
            return (null, null);
        }

        var value = number.Groups[1].Value;
        var n = int.TryParse(value, out var parsed) ? parsed : Array.IndexOf(CountWords, value);
        return (n, "reps");
    }

    private static Scene Beat(SceneType type, string narration, ShotType shot, SfxId? sfx) =>
        new(string.Empty, 0, type, 3, Sentence(narration), null, [], AnimationId.Fade, sfx, null, null, Shot: shot);

    private static ArtSubject? Mistake(StoryboardItem item, IReadOnlyDictionary<string, ArtSubject> subjects) =>
        subjects.TryGetValue(item.Subject, out var s) && s.Kind == ArtKind.Mistake ? s : null;

    // The right-way picture for a mistake (the pose it names) and the other pose of the pair.
    private static (string Asset, string? Partner)? RightPose(ArtSubject mistake, IReadOnlyDictionary<string, ArtSubject> subjects)
    {
        if (mistake.Exercise is null || !subjects.TryGetValue(mistake.Exercise, out var exercise))
        {
            return null;
        }

        if (exercise.Kind != ArtKind.ExercisePair)
        {
            return (exercise.AssetIds[0], null);
        }

        return mistake.Pose == "bottom"
            ? (exercise.AssetIds[1], exercise.AssetIds[0])
            : (exercise.AssetIds[0], exercise.AssetIds[1]);
    }

    private static string? FirstAsset(string subject, IReadOnlyDictionary<string, ArtSubject> subjects) =>
        subjects.TryGetValue(subject, out var s) ? s.AssetIds[0] : null;

    // The on-screen words as they are spoken in the sentence (exact casing), for the caption
    // highlight and the voice's emphasis; null when the sentence doesn't say them.
    private static string? SpokenPhrase(string? narration, string? label)
    {
        var l = label?.Trim().TrimEnd('?', '!', '.');
        if (string.IsNullOrEmpty(narration) || string.IsNullOrEmpty(l) || Words(l) > 3)
        {
            return null;
        }

        var m = Regex.Match(narration, $@"(?<![\w-]){Regex.Escape(l)}(?![\w-])", RegexOptions.IgnoreCase);
        return m.Success ? m.Value : null;
    }

    // The label said in other word forms: "Flared elbows" -> "elbows are flaring", "Squeeze
    // glutes" -> "Squeeze your glutes". Words match on their first four letters; the phrase runs
    // from the first label word the sentence says to the last one within three words, and never
    // across a comma.
    private static string? LoosePhrase(string? narration, string? label)
    {
        if (string.IsNullOrEmpty(narration) || string.IsNullOrEmpty(label))
        {
            return null;
        }

        var stems = LetterWords().Matches(label.ToLowerInvariant())
            .Select(m => m.Value)
            .Where(w => w.Length >= 3 && !LooseStopWords.Contains(w))
            .Select(Stem)
            .ToHashSet();
        var words = LetterWords().Matches(narration).ToList();
        for (var i = 0; i < words.Count; i++)
        {
            if (!stems.Contains(Stem(words[i].Value.ToLowerInvariant())))
            {
                continue;
            }

            var end = i;
            for (var j = i + 1; j < Math.Min(words.Count, i + 3); j++)
            {
                if (stems.Contains(Stem(words[j].Value.ToLowerInvariant())))
                {
                    end = j;
                }
            }

            var phrase = narration[words[i].Index..(words[end].Index + words[end].Length)];
            return phrase.IndexOfAny([',', '.', ';', ':']) >= 0 ? words[i].Value : phrase;
        }

        return null;
    }

    private static readonly HashSet<string> LooseStopWords =
        ["the", "your", "you", "are", "and", "with", "for", "too", "not", "out", "off", "all", "way", "get", "one", "which", "this", "that", "these"];

    private static string Stem(string word) => word.Length <= 4 ? word : word[..4];

    [GeneratedRegex(@"[A-Za-z]+(?:-[A-Za-z]+)*")]
    private static partial Regex LetterWords();

    // "60 seconds" / "20 clean push-ups" as the line says it; null without a number.
    private static string? BenchmarkPhrase(string line)
    {
        var m = Regex.Match(line, @"\b\d+(?: [A-Za-z-]+){1,2}");
        if (!m.Success)
        {
            return null;
        }

        var words = m.Value.Split(' ').ToList();
        // "60 seconds no wobble" -> "60 seconds"; "20 push-ups with" -> "20 push-ups".
        var unit = words.FindIndex(w => BenchmarkUnits.Contains(w.ToLowerInvariant()));
        if (unit > 0)
        {
            words = words[..(unit + 1)];
        }

        while (words.Count > 2 && BenchmarkTails.Contains(words[^1].ToLowerInvariant()))
        {
            words.RemoveAt(words.Count - 1);
        }

        return string.Join(' ', words);
    }

    private static readonly HashSet<string> BenchmarkUnits = ["second", "seconds", "rep", "reps", "minute", "minutes", "times"];
    private static readonly HashSet<string> BenchmarkTails = ["with", "to", "for", "and", "in", "on", "at", "of", "from", "without", "no"];

    public static string Sentence(string text)
    {
        // The LLM sometimes ends a line on a comma ("one of these levels,"): never ",." in the voice.
        var t = Spaces().Replace(text.Replace("\"", string.Empty, StringComparison.Ordinal).Trim(), " ").TrimEnd(',', ';', ':', '-', ' ');
        if (t.Length == 0)
        {
            return t;
        }

        t = char.ToUpperInvariant(t[0]) + t[1..];
        return t[^1] is '.' or '!' or '?' ? t : t + ".";
    }

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private static string? Short(string? text, int maxWords)
    {
        var t = text?.Trim().TrimEnd('.', '!');
        return string.IsNullOrEmpty(t) || Words(t) > maxWords ? null : t;
    }

    private static int Words(string? text) => string.IsNullOrWhiteSpace(text) ? 0 : Spaces().Split(text.Trim()).Length;

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
