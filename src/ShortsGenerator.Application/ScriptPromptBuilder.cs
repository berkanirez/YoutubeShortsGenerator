using System.Text;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Application;

public static class ScriptPromptBuilder
{
    public static string Build(string topic, IReadOnlyList<string>? previousFailureReasons = null)
    {
        var sb = new StringBuilder();

        sb.AppendLine("You are writing the spoken narration for a short (25-45 second), vertical fitness");
        sb.AppendLine("education video. The audience is general fitness enthusiasts, not medical patients.");
        sb.AppendLine();
        sb.AppendLine($"Topic: {topic}");
        sb.AppendLine();
        sb.AppendLine($"Write {ScriptValidator.MinWords}-{ScriptValidator.MaxWords} words of plain spoken English.");
        sb.AppendLine("Rules:");
        sb.AppendLine("- Focus on exercise technique, training principles, or general training knowledge.");
        sb.AppendLine("- Never diagnose, prescribe, promise a cure, or claim to treat a disease or injury.");
        sb.AppendLine("- Use cautious wording for anything uncertain or health-sensitive.");
        sb.AppendLine("- Plain prose only: no markdown, no headings, no bullet points, no emoji, no quotation marks.");
        sb.AppendLine("- Output ONLY the narration text itself, nothing before or after it.");

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
