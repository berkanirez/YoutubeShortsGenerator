using ShortsGenerator.Infrastructure.Processes;

namespace ShortsGenerator.Infrastructure.Tests;

// Runs real child processes. Uses only executables guaranteed on this project's target
// machine (Windows: findstr/ping) or on any machine running these tests (dotnet).
public class ProcessRunnerTests
{
    private readonly ProcessRunner _runner = new();

    [Fact]
    public async Task RunAsync_CapturesExitCodeAndStdout()
    {
        var result = await _runner.RunAsync(
            new ProcessSpec("dotnet", ["--version"], null, TimeSpan.FromSeconds(30)), CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Matches(@"^\d+\.\d+", result.StandardOutput.Trim());
    }

    [Fact]
    public async Task RunAsync_NonZeroExit_IsReturnedNotThrown()
    {
        var result = await _runner.RunAsync(
            new ProcessSpec("dotnet", ["this-command-does-not-exist"], null, TimeSpan.FromSeconds(30)),
            CancellationToken.None);

        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public async Task RunAsync_MissingExecutable_ThrowsProcessLaunchException()
    {
        await Assert.ThrowsAsync<ProcessLaunchException>(() => _runner.RunAsync(
            new ProcessSpec("definitely-not-a-real-program-xyz", [], null, TimeSpan.FromSeconds(5)),
            CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_StandardInput_ArrivesAsUtf8WithoutBom()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // findstr "^" echoes every stdin line unchanged.
        var result = await _runner.RunAsync(
            new ProcessSpec("findstr", ["^"], "Keep your \"core\" tight.", TimeSpan.FromSeconds(30)),
            CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("Keep your \"core\" tight.", result.StandardOutput.Trim());
        Assert.False(result.StandardOutput.StartsWith('﻿'));
    }

    [Fact]
    public async Task RunAsync_ExceedingTimeout_KillsProcessAndThrowsTimeoutException()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var started = DateTime.UtcNow;
        await Assert.ThrowsAsync<TimeoutException>(() => _runner.RunAsync(
            new ProcessSpec("ping", ["-n", "30", "127.0.0.1"], null, TimeSpan.FromMilliseconds(500)),
            CancellationToken.None));

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(10), "the process should be killed, not awaited");
    }

    [Fact]
    public async Task RunAsync_Cancelled_KillsProcessAndThrowsOperationCanceled()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _runner.RunAsync(
            new ProcessSpec("ping", ["-n", "30", "127.0.0.1"], null, TimeSpan.FromMinutes(1)),
            cts.Token));
    }
}
