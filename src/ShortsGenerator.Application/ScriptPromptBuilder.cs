using System.Text;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Application;

public static class ScriptPromptBuilder
{
    public static string Build(string topic, IReadOnlyList<string>? previousFailureReasons = null, StoryFormat? format = null)
    {
        var sb = new StringBuilder();

        sb.AppendLine("You are writing the spoken narration for a short (25-45 second), vertical fitness");
        sb.AppendLine("education video. The audience is general fitness enthusiasts, not medical patients.");
        sb.AppendLine();
        sb.AppendLine($"Topic: {topic}");
        sb.AppendLine();
        sb.AppendLine(
            $"Write {ScriptValidator.TargetMinWords}-{ScriptValidator.TargetMaxWords} words of plain spoken English " +
            $"(never fewer than {ScriptValidator.MinWords} or more than {ScriptValidator.MaxWords}).");
        sb.AppendLine("Use short, complete sentences - one idea per sentence.");
        sb.AppendLine();
        // No example hook sentences on purpose: the small model copies examples word for word.
        sb.AppendLine($"The FIRST sentence is the hook - viewers swipe away within a second. It has at most {HookRule.MaxHookWords} words");
        sb.AppendLine("and speaks directly to the viewer as \"you\": a pointed question about their problem, a surprising");
        sb.AppendLine("claim that challenges what they believe, or the mistake they are probably making right now.");
        sb.AppendLine("Never open with a warm-up such as \"When it comes to\", \"If you're\", \"In this video\", a greeting,");
        sb.AppendLine("or a definition of the exercise. The second sentence says what they will get from watching.");
        sb.AppendLine();
        // Facts alone get swiped; a story shape (StoryFormats) holds the viewer to the end.
        sb.AppendLine("Make it a story, not a list of facts:");
        sb.AppendLine($"- {StoryFormats.Instructions(format ?? StoryFormats.Pick(topic))}");
        sb.AppendLine("- Open a question early and answer it only near the end; use moments they recognize (the last rep, the");
        sb.AppendLine("  number on the bar), not textbook definitions; the last sentence ties back to the hook.");
        sb.AppendLine();
        sb.AppendLine("Rules:");
        sb.AppendLine("- Focus on exercise technique, training principles, or general training knowledge.");
        sb.AppendLine("- Never diagnose, prescribe, promise a cure, or claim to treat a disease or injury.");
        sb.AppendLine("- Use cautious wording for anything uncertain or health-sensitive.");
        sb.AppendLine("- Never invent statistics or studies, and never name real people, channels or brands.");
        sb.AppendLine("- Plain prose only: no markdown, no headings, no bullet points, no emoji, no quotation marks.");
        sb.AppendLine("- Output ONLY the narration text itself, nothing before or after it.");
        // Last on purpose: the story instructions made llama3.2:3b overshoot (168 words), and a
        // small model weighs the end of the prompt most.
        sb.AppendLine();
        sb.AppendLine($"LENGTH: {ScriptValidator.TargetMinWords}-{ScriptValidator.TargetMaxWords} words in total. Anything over " +
                      $"{ScriptValidator.MaxWords} words is rejected - keep every beat to one or two short sentences.");

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
