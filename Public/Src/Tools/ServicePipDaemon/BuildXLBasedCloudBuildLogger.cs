// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.Tracing;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.Interfaces;
using BuildXL.Tracing;
using BuildXL.Tracing.CloudBuild;
using BuildXL.Utilities.Instrumentation.Common;

namespace Tool.ServicePipDaemon
{
    /// <summary>
    ///     Implementation of <see cref="ICloudBuildLogger"/> that uses BuildXL's LoggingContext/>.
    /// </summary>
    public sealed class BuildXLBasedCloudBuildLogger : ICloudBuildLogger
    {
        private readonly IIpcLogger m_localLogger;
        private readonly CloudBuildEventSource m_etwEventSource;

        /// <nodoc/>
        public BuildXLBasedCloudBuildLogger(IIpcLogger logger, bool enableCloudBuildIntegration)
        {
            m_localLogger = logger;
            m_etwEventSource = enableCloudBuildIntegration ? CloudBuildEventSource.Log : CloudBuildEventSource.TestLog;
        }

        /// <inheritdoc/>
        public void Log(DropFinalizationEvent e)
        {
            LogDropEventLocally(e);
            m_etwEventSource.DropFinalizationEvent(e);
        }

        /// <inheritdoc/>
        public void Log(DropCreationEvent e)
        {
            LogDropEventLocally(e);
            m_etwEventSource.DropCreationEvent(e);
        }

        private void LogDropEventLocally(DropOperationBaseEvent e)
        {
            var enabled = ETWLogger.Log.IsEnabled(EventLevel.Verbose, Keywords.CloudBuild) ? "ENABLED" : "DISABLED";
            m_localLogger.Info("Logging {0}Event(dropUrl: {1}, succeeded: {2}): {3}", e.Kind, e.DropUrl, e.Succeeded, enabled);
        }
    }
}
