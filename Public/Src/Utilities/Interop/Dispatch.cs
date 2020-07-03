// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Runtime.InteropServices;
using BuildXL.Interop.Unix;
using static BuildXL.Interop.Windows.Memory;

namespace BuildXL.Interop
{
    /// <summary>
    /// Static class with entry points for common platform interop calls into system facilities
    /// </summary>
    public static class Dispatch
    {
        private static readonly OperatingSystem s_currentOS = CurrentOS();

        /// <summary>
        /// Error code for indicating a successful result from an interop call on macOS
        /// </summary>
        public static int MACOS_INTEROP_SUCCESS = 0x0;

        /// <summary>
        /// Indicates the currently running operating system of the host machine.
        /// </summary>
        public static OperatingSystem CurrentOS()
        {
#if NET_CORE
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return OperatingSystem.Unix;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return OperatingSystem.MacOS;
            }
#endif
            return OperatingSystem.Win;
        }

        /// <summary>
        /// Returns true when executing on OSX.
        /// </summary>
        public static readonly bool IsMacOS = CurrentOS() == OperatingSystem.MacOS;

        /// <summary>
        /// Returns true when executing on Windows.
        /// </summary>
        public static readonly bool IsWinOS = CurrentOS() == OperatingSystem.Win;

        /// <summary>
        /// Gets the elevated status of the process.
        /// </summary>
        /// <returns>True if process is running elevated, otherwise false.</returns>
        public static bool IsElevated() => IsWinOS
            ? Windows.Process.IsElevated()
            : Unix.Process.IsElevated();

        /// <summary>
        /// Checks if a process with id <paramref name="pid"/> exists.
        /// </summary>
        /// <param name="pid">ID of the process to check</param>
        public static bool IsProcessAlive(int pid) => IsWinOS
            ? Windows.Process.IsAlive(pid)
            : Unix.Process.IsAlive(pid);

        /// <summary>
        /// Forcefully terminates a process with id <paramref name="pid"/>.
        /// The return value indicates success.
        /// </summary>
        /// <param name="pid">ID of the process to kill</param>
        public static bool ForceQuit(int pid) => IsWinOS
            ? Windows.Process.ForceQuit(pid)
            : Unix.Process.ForceQuit(pid);

        /// <summary>
        /// Forcefully terminates this process.
        /// </summary>
        public static void ForceQuit() => ForceQuit(System.Diagnostics.Process.GetCurrentProcess().Id);

        /// <summary>
        /// Returns total processor time for a given process.  The process must be running or else an exception is thrown.
        /// </summary>
        public static TimeSpan TotalProcessorTime(System.Diagnostics.Process proc)
        {
            switch (s_currentOS)
            {
                case OperatingSystem.Win:
                    return proc.TotalProcessorTime;

                default:
                    var buffer = new Unix.Process.ProcessResourceUsage();
                    Unix.Process.GetProcessResourceUsage(proc.Id, ref buffer, includeChildProcesses: false);
                    long ticks = (long)(buffer.SystemTimeNs + buffer.UserTimeNs) / 100;
                    return new TimeSpan(ticks);
            }
        }

        /// <summary>
        /// Returns the peak memory usage (in bytes) of a specific process
        /// </summary>
        /// <param name="handle">When calling from Windows the SafeProcessHandle is required</param>
        /// <param name="pid">On non-windows systems a process id has to be provided</param>
        public static ulong? GetActivePeakWorkingSet(IntPtr handle, int pid)
        {
            switch (s_currentOS)
            {
                case OperatingSystem.Win:
                    return Windows.Memory.GetMemoryUsageCounters(handle)?.PeakWorkingSetSize;

                default:
                    ulong peakMemoryUsage = 0;
                    return Unix.Memory.GetPeakWorkingSetSize(pid, ref peakMemoryUsage) == MACOS_INTEROP_SUCCESS
                        ? peakMemoryUsage
                        : (ulong?)null;
            }
        }

        /// <summary>
        /// Returns the memory counters of a specific process
        /// </summary>
        /// <param name="handle">When calling from Windows the SafeProcessHandle is required</param>
        /// <param name="pid">On non-windows systems a process id has to be provided</param>
        public static ProcessMemoryCountersSnapshot? GetMemoryCountersSnapshot(IntPtr handle, int pid)
        {
            switch (s_currentOS)
            {
                case OperatingSystem.Win:
                    var counters = Windows.Memory.GetMemoryUsageCounters(handle);
                    if (counters != null)
                    {
                        return ProcessMemoryCountersSnapshot.CreateFromBytes(
                            counters.PeakWorkingSetSize,
                            counters.WorkingSetSize,
                            (counters.WorkingSetSize + counters.PeakWorkingSetSize) / 2,
                            counters.PeakPagefileUsage,
                            counters.PagefileUsage);
                    }

                    return null;

                default:
                    ulong peakMemoryUsage = 0;
                    if (Unix.Memory.GetPeakWorkingSetSize(pid, ref peakMemoryUsage) == MACOS_INTEROP_SUCCESS)
                    { 
                        return ProcessMemoryCountersSnapshot.CreateFromBytes(
                            peakWorkingSet: peakMemoryUsage,
                            lastWorkingSet: peakMemoryUsage,
                            averageWorkingSet: peakMemoryUsage,
                            peakCommitSize: 0,
                            lastCommitSize: 0);
                    }
                    
                    return null;
            }
        }
    }
}
