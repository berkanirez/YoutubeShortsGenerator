using System.Text.Json;
using Microsoft.Extensions.Options;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Infrastructure.Storage;

public sealed class FileVideoPlanStore : IVideoPlanStore
{
    private const string FileName = "plan.json";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _rootDirectory;

    public FileVideoPlanStore(IOptions<OutputStorageOptions> options)
    {
        _rootDirectory = options.Value.RootDirectory;
    }

    public async Task<string> SaveAsync(Guid jobId, VideoPlan plan, CancellationToken cancellationToken)
    {
        var jobDirectory = GetJobDirectory(jobId);
        Directory.CreateDirectory(jobDirectory);

        var filePath = Path.Combine(jobDirectory, FileName);
        await using var stream = File.Create(filePath);
        await JsonSerializer.SerializeAsync(stream, plan, JsonOptions, cancellationToken);

        return jobDirectory;
    }

    public async Task<VideoPlan?> LoadAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var filePath = Path.Combine(GetJobDirectory(jobId), FileName);
        if (!File.Exists(filePath))
        {
            return null;
        }

        await using var stream = File.OpenRead(filePath);
        return await JsonSerializer.DeserializeAsync<VideoPlan>(stream, JsonOptions, cancellationToken);
    }

    // jobId is always a Guid (never user-supplied text), so this can never escape
    // _rootDirectory via path traversal - the CLAUDE.md section 18 "sanitize filenames"
    // rule is satisfied by construction here, not by extra runtime checks.
    private string GetJobDirectory(Guid jobId) => Path.Combine(_rootDirectory, jobId.ToString());
}
