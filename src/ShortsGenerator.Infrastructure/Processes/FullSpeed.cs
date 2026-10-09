using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ShortsGenerator.Infrastructure.Processes;

// Day 29, found by measuring: the same render took 22s from a normal shell and 61s (CPU at
// 10-37%) when the API had been started in a minimized window (generate.cmd does that).
// Windows 11 treats processes of a minimized/background console as "background" and runs
// them in EcoQoS (efficiency mode, power throttling) - and Kokoro and Remotion, started by
// the API, with them. A video job is work the user is waiting for, so the API and every child
// it starts opt out: "execution speed throttling: off".
internal static class FullSpeed
{
    private const int ProcessPowerThrottling = 4;
    private const uint ThrottlingExecutionSpeed = 0x1;

    public static void ApplyToCurrentProcess()
    {
        using var self = Process.GetCurrentProcess();
        Apply(self);
    }

    public static void Apply(Process process)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var state = new PROCESS_POWER_THROTTLING_STATE
        {
            Version = 1,
            ControlMask = ThrottlingExecutionSpeed,   // "I decide about execution speed throttling"
            StateMask = 0,                            // "... and it is off"
        };
        try
        {
            // Best effort: on older Windows versions this information class doesn't exist.
            SetProcessInformation(process.Handle, ProcessPowerThrottling, ref state, (uint)Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>());
        }
        catch (InvalidOperationException)
        {
            // The process already exited.
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_POWER_THROTTLING_STATE
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessInformation(
        IntPtr hProcess, int processInformationClass, ref PROCESS_POWER_THROTTLING_STATE processInformation, uint size);
}
