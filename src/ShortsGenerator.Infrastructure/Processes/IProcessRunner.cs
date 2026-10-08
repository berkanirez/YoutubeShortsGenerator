namespace ShortsGenerator.Infrastructure.Processes;

/// <param name="FileName">Executable to start - never a shell, so nothing is ever parsed as a command line.</param>
/// <param name="Arguments">Passed one-by-one via ProcessStartInfo.ArgumentList (CLAUDE.md section 18).</param>
/// <param name="StandardInput">Written to stdin as UTF-8 (no BOM), then stdin is closed. Null = no input.</param>
public sealed record ProcessSpec(
    string FileName,
    IReadOnlyList<string> Arguments,
    string? StandardInput,
    TimeSpan Timeout,
    string? WorkingDirectory = null);

public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError, TimeSpan Elapsed);

// Infrastructure-internal seam: adapters (Kokoro today, Remotion on Day 13) build a
// ProcessSpec and interpret the result; this interface lets their tests fake the
// process instead of needing Python/Node installed.
public interface IProcessRunner
{
    /// <exception cref="ProcessLaunchException">The executable could not be started.</exception>
    /// <exception cref="TimeoutException">The process exceeded spec.Timeout and was killed.</exception>
    /// <exception cref="OperationCanceledException">Cancelled; the process was killed.</exception>
    Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken);
}

public sealed class ProcessLaunchException : Exception
{
    public ProcessLaunchException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
