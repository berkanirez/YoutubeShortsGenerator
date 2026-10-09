using ShortsGenerator.Domain;

namespace ShortsGenerator.Application.Tests;

// Writes real files under a TempJobOutputLocator (review state depends on real file times)
// and records what was written.
internal sealed class TempArtifactWriter : IJobArtifactWriter
{
    private readonly IJobOutputLocator _locator;

    public TempArtifactWriter(IJobOutputLocator locator)
    {
        _locator = locator;
    }

    public List<(string FileName, string Content)> Written { get; } = [];

    public string? Index { get; private set; }

    public async Task<string> WriteTextAsync(Guid jobId, string fileName, string content, CancellationToken cancellationToken)
    {
        Written.Add((fileName, content));
        var directory = _locator.GetJobDirectory(jobId);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        await File.WriteAllTextAsync(path, content, cancellationToken);
        return path;
    }

    public async Task<string?> ReadTextAsync(Guid jobId, string fileName, CancellationToken cancellationToken)
    {
        var path = Path.Combine(_locator.GetJobDirectory(jobId), fileName);
        return File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : null;
    }

    public Task<string> WriteOutputIndexAsync(string content, CancellationToken cancellationToken)
    {
        Index = content;
        return Task.FromResult("INDEX.md");
    }
}
