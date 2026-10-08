namespace ShortsGenerator.Domain;

// Text-to-speech behind an interface (CLAUDE.md section 9): today a local Kokoro
// process, later possibly a premium service - callers never know which.
public interface ITtsProvider
{
    Task<TtsResult> SynthesizeAsync(TtsRequest request, CancellationToken cancellationToken);

    Task<TtsAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken);
}

/// <param name="Segments">
/// Text spoken back to back into one audio file (one per narrated scene). Synthesizing
/// them separately is what lets the provider report exactly where each one starts.
/// </param>
/// <param name="OutputPath">Absolute path of the .wav file to create (or overwrite).</param>
/// <param name="Voice">Overrides the configured default voice when set.</param>
public sealed record TtsRequest(IReadOnlyList<string> Segments, string OutputPath, string? Voice = null);

/// <param name="Segments">Same order as the request's segments.</param>
public sealed record TtsResult(
    string AudioPath,
    double DurationSeconds,
    int SampleRate,
    string Voice,
    IReadOnlyList<NarrationSegmentTiming> Segments);

/// <param name="SetupHint">Engine-specific "how to fix it" text, shown to the user alongside Problems.</param>
public sealed record TtsAvailability(bool IsReady, string Engine, IReadOnlyList<string> Problems, string? SetupHint = null);
