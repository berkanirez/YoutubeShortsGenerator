using ShortsGenerator.Infrastructure.Processes;

namespace ShortsGenerator.Infrastructure.Tests;

internal sealed class FakeProcessRunner : IProcessRunner
{
    private readonly Func<ProcessSpec, ProcessResult> _behavior;

    public FakeProcessRunner(Func<ProcessSpec, ProcessResult> behavior)
    {
        _behavior = behavior;
    }

    public List<ProcessSpec> Calls { get; } = [];

    public static FakeProcessRunner Returning(int exitCode, string stdout = "", string stderr = "") =>
        new(_ => new ProcessResult(exitCode, stdout, stderr, TimeSpan.FromSeconds(1)));

    public static FakeProcessRunner Throwing(Exception exception) => new(_ => throw exception);

    public Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken)
    {
        Calls.Add(spec);
        return Task.FromResult(_behavior(spec));
    }
}
