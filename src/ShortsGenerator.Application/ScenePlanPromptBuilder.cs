using System.Text;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Application;

public static class ScenePlanPromptBuilder
{
    public static string Build(string topic, string narrationText, IReadOnlyList<string>? previousFailureReasons = null)
    {
        var sceneTypes = string.Join(", ", Enum.GetNames<SceneType>());

        var sb = new StringBuilder();

        sb.AppendLine("You are turning an already-written narration into a structured scene plan");
        sb.AppendLine("for a short (25-45 second), vertical fitness education video.");
        sb.AppendLine();
        sb.AppendLine($"Topic: {topic}");
        sb.AppendLine();
        sb.AppendLine("Narration (do not change a single word of this - split it into ordered pieces,");
        sb.AppendLine("one or more consecutive sentences per scene):");
        sb.AppendLine("\"\"\"");
        sb.AppendLine(narrationText);
        sb.AppendLine("\"\"\"");
        sb.AppendLine();
        sb.AppendLine("Split the narration above into 6 to 10 scenes, in order. Respond with ONLY a single");
        sb.AppendLine("JSON object (no markdown, no code fences, no commentary), shaped exactly like this:");
        sb.AppendLine("""
            {
              "title": "short video title",
              "hook": "one punchy sentence used as the opening hook",
              "targetDurationSeconds": 30,
              "style": null,
              "captionStyle": null,
              "musicMood": null,
              "scenes": [
                {
                  "id": "scene-1",
                  "order": 1,
                  "type": "Hook",
                  "durationSeconds": 4,
                  "narration": "the exact words of the narration for this scene",
                  "onScreenText": "short on-screen caption or null",
                  "assetIds": [],
                  "animation": "punchZoom",
                  "sfx": null,
                  "background": null,
                  "emphasis": null
                }
              ]
            }
            """);
        sb.AppendLine();
        sb.AppendLine($"Allowed values for \"type\" (use the exact spelling): {sceneTypes}.");
        sb.AppendLine("Allowed values for \"animation\": fade, slide, pop, punchZoom, shake, bounce, pulse, counter, highlight.");
        sb.AppendLine("Allowed values for \"sfx\" (optional, use null if none fits): pop, whoosh, error, success, impact, click.");
        sb.AppendLine("Rules:");
        sb.AppendLine("- Exactly one scene must have type \"Hook\" and it must be first (order 1).");
        sb.AppendLine("- Exactly one scene must have type \"Outro\" and it must be last.");
        sb.AppendLine("- Every scene's \"narration\" must be copied verbatim from the narration above -");
        sb.AppendLine("  concatenating all scenes' narration, in order, must reproduce it exactly.");
        sb.AppendLine("- \"durationSeconds\" must be realistic for how long that piece of narration takes to");
        sb.AppendLine("  say out loud (roughly 2-3 words per second), and the scenes' durations must add up");
        sb.AppendLine("  close to \"targetDurationSeconds\".");
        sb.AppendLine("- \"assetIds\" may be an empty array - a real asset library does not exist yet.");
        sb.AppendLine("- If the narration has no natural closing line left over for the \"Outro\" scene, that");
        sb.AppendLine("  scene may have \"narration\": null (a silent, visual-only closing card) - do not");
        sb.AppendLine("  invent new narration text to fill it.");

        if (previousFailureReasons is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("Your previous attempt was rejected for these reasons - fix all of them this time:");
            foreach (var reason in previousFailureReasons)
            {
                sb.AppendLine($"- {reason}");
            }
        }

        return sb.ToString();
    }
}
