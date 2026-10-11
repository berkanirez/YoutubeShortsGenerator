namespace ShortsGenerator.Domain;

public enum VideoJobStatus
{
    Created,
    Planning,
    Narrating,
    Rendering,
    Completed,
    Failed,

    // The plan needs pictures the catalog doesn't have (ArtGapDetector); the job waits until
    // the owner adds them (or continues without), then goes on to narration.
    WaitingForArt,
}
