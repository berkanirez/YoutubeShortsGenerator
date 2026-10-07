namespace ShortsGenerator.Domain;

public sealed class LlmUnavailableException : Exception
{
    public LlmUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
