// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Tracing;
using BuildXL.Utilities.Instrumentation.Common;

#pragma warning disable 1591
#nullable enable

namespace BuildXL.SandboxedProcessExecutor.Tracing
{
    /// <summary>
    /// Logging for executor.
    /// There are no log files, so messages for events with <see cref="EventGenerators.LocalOnly"/> will be lost.
    /// </summary>
    [EventKeywordsType(typeof(Keywords))]
    [EventTasksType(typeof(Tasks))]
    [LoggingDetails("SandboxProcessExecutorLogger")]
    public abstract partial class Logger : LoggerBase
    {
        /// <summary>
        /// Returns the logger instance
        /// </summary>
        public static Logger Log => m_log;

        [GeneratedEvent(
            (int)LogEventId.SandboxedProcessExecutorInvoked,
            EventGenerators = EventGenerators.TelemetryOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.SandboxedProcessExecutor,
            Message = "Invocation")]
        public abstract void SandboxedProcessExecutorInvoked(LoggingContext context, long runtimeMs, string commandLine);

        [GeneratedEvent(
            (int)LogEventId.SandboxedProcessExecutorCatastrophicFailure,
            EventGenerators = EventGenerators.TelemetryOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.SandboxedProcessExecutor,
            Message = "Catastrophic failure")]
        public abstract void SandboxedProcessExecutorCatastrophicFailure(LoggingContext context, string exceptionMessage);
    }
}
