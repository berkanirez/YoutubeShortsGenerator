using System.Globalization;
using System.Text;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Application;

// The human review step CLAUDE.md section 4 requires: V1 never uploads anything, and every
// finished video gets a review.md next to it - what the automation checked, what it
// wasn't sure about, and what only a person can judge before publishing.
public static class ReviewChecklist
{
    public const string FileName = "review.md";

    // The human half of the review (CLAUDE.md section 4: a human review step before
    // publishing). One list for review.md and for the dashboard's tick boxes (Day 28).
    public static IReadOnlyList<string> HumanChecks { get; } =
    [
        "Every claim is accurate and cautiously worded - no medical or treatment claims.",
        "The narration sounds natural; no mispronounced words or odd pauses.",
        "Captions match the voice and are readable on a phone.",
        "Each scene's visual fits what is being said at that moment.",
        "Music and sound effects never cover the voice.",
        "It teaches something an earlier video didn't (takeaway above).",
        "Title and description are yours to write; nothing is posted automatically.",
    ];

    public static string Build(VideoJob job, VideoPlan plan, DateTimeOffset renderedAt)
    {
        var scenes = plan.Scenes.OrderBy(s => s.Order).ToList();
        var sb = new StringBuilder();
        var inv = CultureInfo.InvariantCulture;

        sb.AppendLine($"# Review before publishing: {plan.Title}");
        sb.AppendLine();
        sb.AppendLine($"- Topic: {job.Topic}");
        sb.AppendLine($"- Job: `{job.Id}`");
        sb.AppendLine(inv, $"- Rendered: {renderedAt:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine(inv, $"- Length: {plan.TargetDurationSeconds:0.0}s, {scenes.Count} scenes, music: {plan.MusicMood ?? "none"}");
        sb.AppendLine($"- Takeaway: {plan.Takeaway ?? "(none)"}");
        sb.AppendLine();
        sb.AppendLine("## Automatic checks");
        sb.AppendLine();
        sb.AppendLine("Passed: narration reproduced exactly, 6-10 scenes, Hook first / Outro last, only catalog assets,");
        sb.AppendLine("script length and medical-claim filter, timing derived from the real audio.");
        sb.AppendLine();
        if (plan.ReviewWarnings is { Count: > 0 })
        {
            sb.AppendLine("Warnings the plan was accepted with (look at these first):");
            foreach (var warning in plan.ReviewWarnings)
            {
                sb.AppendLine($"- ⚠ {warning}");
            }
        }
        else
        {
            sb.AppendLine("No variety or originality warnings.");
        }

        sb.AppendLine();
        sb.AppendLine("## Scenes");
        sb.AppendLine();
        sb.AppendLine("| # | Type | On screen | Asset |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var scene in scenes)
        {
            var onScreen = string.Join(" / ", new[] { scene.OnScreenText, scene.SecondaryText }.Where(t => !string.IsNullOrWhiteSpace(t)));
            if (scene.Items is { Count: > 0 })
            {
                onScreen += $" [{string.Join(", ", scene.Items)}]";
            }

            sb.AppendLine($"| {scene.Order} | {scene.Type} | {Cell(onScreen)} | {Cell(string.Join(", ", scene.AssetIds))} |");
        }

        sb.AppendLine();
        sb.AppendLine("## Your checklist (V1 never uploads - publishing is your decision)");
        sb.AppendLine();
        foreach (var check in HumanChecks)
        {
            sb.AppendLine($"- [ ] {check}");
        }

        sb.AppendLine("(Tick them in the dashboard's review box: a video counts as reviewed once all are ticked.)");
        sb.AppendLine();
        sb.AppendLine("## Script");
        sb.AppendLine();
        sb.AppendLine(plan.NarrationText);
        return sb.ToString();
    }

    // Keeps a table cell on one row.
    private static string Cell(string text) => string.IsNullOrWhiteSpace(text) ? "-" : text.Replace("|", "/").ReplaceLineEndings(" ");
}
