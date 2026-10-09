using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace ShortsGenerator.Infrastructure.Processes;

public sealed class ProcessRunner : IProcessRunner
{
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public async Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = spec.FileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = spec.StandardInput is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        if (spec.StandardInput is not null)
        {
            // Without this, .NET may prepend a BOM - which a Python reader then sees as a
            // real (invisible) character at the start of the narration.
            startInfo.StandardInputEncoding = Utf8NoBom;
        }

        if (spec.WorkingDirectory is not null)
        {
            startInfo.WorkingDirectory = spec.WorkingDirectory;
        }

        foreach (var argument in spec.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        var stopwatch = Stopwatch.StartNew();
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new ProcessLaunchException($"Could not start '{spec.FileName}': {ex.Message}", ex);
        }

        // Dies with us, even if we crash (see KillOnExitJob), and isn't slowed down by
        // Windows' efficiency mode when our window is minimized (see FullSpeed).
        KillOnExitJob.Add(process);
        FullSpeed.Apply(process);

        // Both pipes are drained concurrently from the start: if the child fills the stderr
        // buffer while we're only reading stdout (or vice versa), both sides wait forever.
        var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

        using var timeoutCts = new CancellationTokenSource(spec.Timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        try
        {
            if (spec.StandardInput is not null)
            {
                await process.StandardInput.WriteAsync(spec.StandardInput.AsMemory(), linkedCts.Token);
                process.StandardInput.Close();
            }

            await process.WaitForExitAsync(linkedCts.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException)
        {
            // IOException: the child exited/closed stdin before we finished writing. Let it
            // fall through to the normal exit handling unless we were cancelled/timed out.
            if (!linkedCts.IsCancellationRequested)
            {
                await process.WaitForExitAsync(CancellationToken.None);
            }
            else
            {
                KillTree(process);
                cancellationToken.ThrowIfCancellationRequested();
                throw new TimeoutException(
                    $"'{Path.GetFileName(spec.FileName)}' did not finish within {spec.Timeout.TotalSeconds:0}s and was stopped.");
            }
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        return new ProcessResult(process.ExitCode, stdout, stderr, stopwatch.Elapsed);
    }

    private static void KillTree(Process process)
    {
        try
        {
            // Entire tree: python/node may have spawned workers of their own.
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }
        catch (InvalidOperationException)
        {
            // Already exited between the timeout firing and Kill - nothing to do.
        }
    }
}
