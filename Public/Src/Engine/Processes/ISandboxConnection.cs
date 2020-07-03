// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Configuration;
using System.Collections.Generic;

namespace BuildXL.Processes
{
    /// <summary>
    /// Interface for sandbox connections, used to establish a connection
    /// and manage communication between BuildXL and a sandbox implementation.
    /// </summary>
    public interface ISandboxConnection : IDisposable
    {
        /// <summary>
        /// The sandbox kind used by the backing SandboxConnection, e.g. MacOsKext
        /// </summary>
        SandboxKind Kind { get; }

        /// <summary>
        /// Whether to measure CPU times (user/system) of sandboxed processes.  Default: false.
        /// </summary>
        /// <remarks>
        /// In principle thre should be no reason not to measure CPU times.  This amounts to wrapping every
        /// process in '/usr/bin/time', which could (potentially) lead to some unexpected behavior.
        /// </remarks>
        bool MeasureCpuTimes { get; }

        /// <summary>
        /// Reports the earliest (minimum) enqueue time received from all the sandbox report queues available
        /// </summary>
        ulong MinReportQueueEnqueueTime { get; }

        /// <summary>
        /// Timespan between now and when the last report was received (from any queue).
        /// </summary>
        TimeSpan CurrentDrought { get; }

        /// <summary>
        /// Notifies the sandbox of:
        ///   (1) the current CPU usage (in basis points), and
        ///   (2) amount of available physical memory (in megabytes).
        /// CPU usage is normalized across all cores (i.e., this number should be between 0 and 10000,
        /// which corresponds to 0% and 100%).
        /// </summary>
        /// <remarks>
        /// This method wouldn't be necessary if the sandbox could obtain 'host_statistics' on its own;
        /// with the current implementation, unfortunately, it appears that there is no way for it to do so.
        /// </remarks>
        bool NotifyUsage(uint cpuUsageBasisPoints, uint availableRamMB);

        /// <summary>
        /// Notifies the sandbox that a new pip is about to start. Since the sandbox expects to receive the
        /// process ID of the pip, this method requires that the supplied <paramref name="process"/> has already been started,
        /// and hence already has an ID assigned to it. To ensure that the process is not going to request file accesses before the
        /// sandbox is notified about it being started, the process should be started in some kind of suspended mode, and
        /// resumed only after the sandbox has been notified.
        /// </summary>
        bool NotifyPipStarted(LoggingContext loggingContext, FileAccessManifest fam, SandboxedProcessUnix process);

        /// <summary>
        /// A concrete sandbox connection can override this method to specify additional environment variables
        /// that should be set before executing the process.
        /// </summary>
        public IEnumerable<(string, string)> AdditionalEnvVarsToSet(long pipId);

        /// <summary>
        /// SandboxedProcess uses this method to notify the connection that the root process of the pip exited.
        /// </summary>
        void NotifyRootProcessExited(long pipId, SandboxedProcessUnix process);

        /// <summary>
        /// Notifies the sandbox that <paramref name="process"/> is done processing access reports
        /// for Pip <paramref name="pipId"/> so that resources can be freed up.
        /// Returns whether the sandbox was successfully notified and cleaned up all resources
        /// for the pip with <paramref name="pipId"/>d.
        /// </summary>
        bool NotifyPipFinished(long pipId, SandboxedProcessUnix process);

        /// <summary>
        /// Notification that a pip process was forcefully terminated.
        /// </summary>
        void NotifyPipProcessTerminated(long pipId, int processId);

        /// <summary>
        /// Releases all resources held by the sandbox connection including all unmanaged references too. This is only for unit testing and should not
        /// be called directly at any time! Unit tests need this as they reference a static sandbox connection instance that is torn down on process exit.
        /// </summary>
        void ReleaseResources();

        /// <summary>
        /// Indicates if the SandboxConnection is running for unit-test mode.
        /// </summary>
        bool IsInTestMode { get; }
    }
}
