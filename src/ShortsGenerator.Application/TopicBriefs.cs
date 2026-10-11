using System.Text.RegularExpressions;

namespace ShortsGenerator.Application;

/// <summary>What a topic is really about, and the mainstream points a video on it should build on.</summary>
public sealed record TopicBrief(string Id, string About, IReadOnlyList<string> Points);

// A 3B model asked for "Do This and Get Shredded" wrote about lifting form - it doesn't know
// what the topic needs. For the common lifestyle topics the key points are written here, in
// cautious mainstream wording, and the script must be built on them. Matched on the topic's
// words; the first matching brief wins.
public static partial class TopicBriefs
{
    public static readonly IReadOnlyList<(string[] Words, TopicBrief Brief)> All =
    [
        (["shred", "shredded", "lean", "fat loss", "lose fat", "burn fat", "belly fat", "cut", "cutting", "lose weight", "weight loss", "body fat", "get ripped", "ripped"],
         new TopicBrief("fat-loss", "losing body fat so the muscles show (getting lean / shredded)",
         [
             "a steady calorie deficit is what lowers body fat; no exercise burns fat from one spot",
             "protein at every meal keeps muscle while dieting and keeps hunger down",
             "keep lifting weights so the body holds on to its muscle",
             "walking more every day adds up to more than a short cardio session",
             "sleep matters: short sleep makes hunger harder to control",
             "lose it slowly - quick crash diets usually come back",
         ])),
        (["six pack", "abs", "ab", "stomach", "core"],
         new TopicBrief("abs", "getting visible abs",
         [
             "abs show when body fat is low enough; endless crunches alone don't reveal them",
             "train the core with a few hard exercises like hanging leg raises and planks, not hundreds of reps",
             "a calorie deficit and enough protein uncover the abs you already have",
             "heavy compound lifts also work the core",
         ])),
        (["bulk", "bulking", "gain muscle", "build muscle", "muscle growth", "grow", "bigger", "mass", "hypertrophy", "gains", "size"],
         new TopicBrief("muscle-gain", "building muscle",
         [
             "progressive overload: add a rep or a little weight over time",
             "enough protein spread over the day",
             "a small calorie surplus, not eating everything in sight",
             "train each muscle about twice a week, close to failure",
             "muscles grow while you rest and sleep, not during the workout",
         ])),
        (["sleep", "recovery", "rest day", "rest days", "overtraining", "sore", "soreness"],
         new TopicBrief("recovery", "recovery and sleep for training",
         [
             "muscle is rebuilt during rest, not during the workout",
             "most adults do best with seven to nine hours of sleep",
             "rest days and lighter weeks let you come back stronger",
             "constant soreness and dropping strength are signs you need more recovery",
         ])),
        (["protein", "diet", "nutrition", "eat", "eating", "meal", "meals", "food"],
         new TopicBrief("nutrition", "eating for training",
         [
             "protein at every meal supports muscle repair",
             "total calories decide whether you gain or lose weight",
             "mostly whole foods: protein, vegetables, fruit, whole grains",
             "supplements only fill gaps; food comes first",
         ])),
        (["cardio", "running", "run", "conditioning", "endurance", "steps"],
         new TopicBrief("cardio", "cardio and fitness",
         [
             "cardio strengthens the heart and helps recovery between sets",
             "a moderate amount of cardio does not erase strength gains",
             "walking and easy cardio are simple ways to burn more energy",
             "build up gradually to avoid overuse injuries",
         ])),
    ];

    public static TopicBrief? Find(string topic)
    {
        var text = " " + NonWord().Replace(topic.ToLowerInvariant(), " ") + " ";
        return All.FirstOrDefault(entry => entry.Words.Any(w => text.Contains($" {w} ", StringComparison.Ordinal))).Brief;
    }

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonWord();
}
