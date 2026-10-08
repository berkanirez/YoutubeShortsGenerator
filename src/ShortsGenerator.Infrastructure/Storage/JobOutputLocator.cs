using Microsoft.Extensions.Options;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Infrastructure.Storage;

public sealed class JobOutputLocator : IJobOutputLocator
{
    private readonly string _rootDirectory;

    public JobOutputLocator(IOptions<OutputStorageOptions> options)
    {
        _rootDirectory = Path.GetFullPath(options.Value.RootDirectory);
    }

    // jobId is always a Guid (never user-supplied text), so this can never escape
    // _rootDirectory via path traversal - the CLAUDE.md section 18 "sanitize filenames"
    // rule is satisfied by construction here, not by extra runtime checks.
    public string GetJobDirectory(Guid jobId) => Path.Combine(_rootDirectory, jobId.ToString());
}
