using Microsoft.Extensions.Options;
using ShortsGenerator.Infrastructure.Storage;

namespace ShortsGenerator.Infrastructure.Tests;

public sealed class FileJobArtifactWriterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "artifact-tests-" + Guid.NewGuid());
    private readonly FileJobArtifactWriter _writer;

    public FileJobArtifactWriterTests()
    {
        _writer = new FileJobArtifactWriter(new JobOutputLocator(Options.Create(new OutputStorageOptions { RootDirectory = _root })));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task WriteTextAsync_WritesUtf8WithoutBomIntoTheJobFolder()
    {
        var jobId = Guid.NewGuid();

        var path = await _writer.WriteTextAsync(jobId, "review.md", "⚠ çalış", CancellationToken.None);

        Assert.Equal(Path.Combine(_root, jobId.ToString(), "review.md"), path);
        var bytes = await File.ReadAllBytesAsync(path);
        Assert.False(bytes is [0xEF, 0xBB, 0xBF, ..]);
        Assert.Equal("⚠ çalış", await File.ReadAllTextAsync(path));
    }

    [Theory]
    [InlineData("../escape.md")]
    [InlineData("..\\escape.md")]
    [InlineData("sub/review.md")]
    [InlineData("C:\\temp\\x.md")]
    [InlineData("review")]
    [InlineData(".hidden.md")]
    public async Task WriteTextAsync_RejectsAnythingThatIsNotAPlainFileName(string fileName)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _writer.WriteTextAsync(Guid.NewGuid(), fileName, "x", CancellationToken.None));
        Assert.False(Directory.Exists(_root) && Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).Any());
    }
}
