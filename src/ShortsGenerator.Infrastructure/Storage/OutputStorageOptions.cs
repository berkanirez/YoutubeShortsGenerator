namespace ShortsGenerator.Infrastructure.Storage;

public sealed class OutputStorageOptions
{
    public const string SectionName = "OutputStorage";

    // Relative to the Api project's content root (src/ShortsGenerator.Api), so this is the
    // repository-level output/ folder CLAUDE.md expects (output/<job-id>/final.mp4).
    public string RootDirectory { get; set; } = "../../output";
}
