namespace ShortsGenerator.Domain;

// The TTS engine isn't installed/configured (missing Python, model files, ...): an
// environment problem the user fixes once, not a problem with this particular job.
public sealed class TtsUnavailableException : Exception
{
    public TtsUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

// The engine is there but failed to produce audio for this text.
public sealed class TtsGenerationException : Exception
{
    public TtsGenerationException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
