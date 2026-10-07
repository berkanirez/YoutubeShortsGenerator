using ShortsGenerator.Domain;

namespace ShortsGenerator.Api.Contracts;

public sealed record CreateVideoJobRequest(string Topic, string? SourceScript);

public sealed record VideoJobResponse(
    Guid Id,
    string Topic,
    string? SourceScript,
    string? GeneratedScript,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? ErrorMessage,
    string? FinalVideoPath)
{
    public static VideoJobResponse FromDomain(VideoJob job) => new(
        job.Id,
        job.Topic,
        job.SourceScript,
        job.GeneratedScript,
        job.Status.ToString(),
        job.CreatedAt,
        job.UpdatedAt,
        job.ErrorMessage,
        job.FinalVideoPath);
}
