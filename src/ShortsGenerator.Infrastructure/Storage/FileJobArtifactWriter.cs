using System.Text;
using System.Text.RegularExpressions;
using ShortsGenerator.Domain;

namespace ShortsGenerator.Infrastructure.Storage;

public sealed partial class FileJobArtifactWriter : IJobArtifactWriter
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
    private readonly IJobOutputLocator _locator;

    public FileJobArtifactWriter(IJobOutputLocator locator)
    {
        _locator = locator;
    }

    public async Task<string> WriteTextAsync(Guid jobId, string fileName, string content, CancellationToken cancellationToken)
    {
        var directory = _locator.GetJobDirectory(jobId);
        Directory.CreateDirectory(directory);
        var path = PathFor(jobId, fileName);
        await File.WriteAllTextAsync(path, content, Utf8NoBom, cancellationToken);
        return path;
    }

    public async Task<string?> ReadTextAsync(Guid jobId, string fileName, CancellationToken cancellationToken)
    {
        var path = PathFor(jobId, fileName);
        return File.Exists(path) ? await File.ReadAllTextAsync(path, cancellationToken) : null;
    }

    public async Task<string> WriteOutputIndexAsync(string content, CancellationToken cancellationToken)
    {
        // The output root is the job folders' parent: a fixed name, never anything from a job.
        var root = Path.GetDirectoryName(_locator.GetJobDirectory(Guid.Empty))!;
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "INDEX.md");
        var temp = path + ".tmp";
        await File.WriteAllTextAsync(temp, content, Utf8NoBom, cancellationToken);
        File.Move(temp, path, overwrite: true);
        return path;
    }

    // CLAUDE.md section 18 (sanitize filenames, keep files under known directories): only
    // a plain name made of safe characters, so nothing can point outside the job folder.
    private string PathFor(Guid jobId, string fileName)
    {
        if (!SafeFileName().IsMatch(fileName))
        {
            throw new ArgumentException($"'{fileName}' is not a plain file name.", nameof(fileName));
        }

        return Path.Combine(_locator.GetJobDirectory(jobId), fileName);
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_-]*\.[A-Za-z0-9]{1,8}$")]
    private static partial Regex SafeFileName();
}
