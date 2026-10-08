using ShortsGenerator.Domain;

namespace ShortsGenerator.Application.Tests;

internal sealed class FakeRenderer : IRenderer
{
    public bool IsReady { get; init; } = true;
    public Exception? Error { get; init; }
    public List<RenderRequest> Requests { get; } = [];

    public Task<RendererAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken) =>
        Task.FromResult(IsReady
            ? new RendererAvailability(true, "fake", [])
            : new RendererAvailability(false, "fake", ["node_modules missing"], "run npm install"));

    public Task<RenderResult> RenderAsync(RenderRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Error is null
            ? Task.FromResult(new RenderResult(request.OutputPath, 1234, TimeSpan.FromSeconds(60)))
            : Task.FromException<RenderResult>(Error);
    }
}

// A real, empty temp folder per test class, so services that check files on disk work.
internal sealed class TempJobOutputLocator : IJobOutputLocator, IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "shorts-app-tests-" + Guid.NewGuid());

    public string GetJobDirectory(Guid jobId) => Path.Combine(_root, jobId.ToString());

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
