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
        // CLAUDE.md section 18 (sanitize filenames, keep files under known directories): only
        // a plain name made of safe characters, so nothing can point outside the job folder.
        if (!SafeFileName().IsMatch(fileName))
        {
            throw new ArgumentException($"'{fileName}' is not a plain file name.", nameof(fileName));
        }

        var directory = _locator.GetJobDirectory(jobId);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        await File.WriteAllTextAsync(path, content, Utf8NoBom, cancellationToken);
        return path;
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_-]*\.[A-Za-z0-9]{1,8}$")]
    private static partial Regex SafeFileName();
}
