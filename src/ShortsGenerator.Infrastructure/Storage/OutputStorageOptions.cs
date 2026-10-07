namespace ShortsGenerator.Infrastructure.Storage;

public sealed class OutputStorageOptions
{
    public const string SectionName = "OutputStorage";

    public string RootDirectory { get; set; } = "output";
}
