namespace ShortsGenerator.Domain;

public sealed class VideoPlanGenerationException : Exception
{
    public VideoPlanGenerationException(string message)
        : base(message)
    {
    }
}
