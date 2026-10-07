namespace ShortsGenerator.Domain;

public sealed class ScriptGenerationException : Exception
{
    public ScriptGenerationException(string message)
        : base(message)
    {
    }
}
