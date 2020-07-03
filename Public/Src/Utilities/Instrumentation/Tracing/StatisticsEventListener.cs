// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Diagnostics.ContractsLight;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.IO;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Tracing
{
    /// <nodoc />
    public sealed class StatisticsEventListener : TextWriterEventListener
    {
        private readonly LoggingContext m_loggingContext;
        private readonly ConcurrentDictionary<string, long> m_finalStatistics = new ConcurrentDictionary<string, long>();

        /// <summary>
        /// Creates a new instance configured to output to the given writer.
        /// </summary>
        public StatisticsEventListener(Events eventSource, TextWriter writer, LoggingContext loggingContext, DisabledDueToDiskWriteFailureEventHandler? onDisabledDueToDiskWriteFailure = null)
            : base(
                eventSource,
                writer,
                DateTime.UtcNow, // The time doesn't actually matter since no times are written to the stats log
                warningMapper: null,
                level: EventLevel.Verbose,
                eventMask: new EventMask(enabledEvents: new[] { (int)LogEventId.Statistic, (int)LogEventId.StatisticWithoutTelemetry }, disabledEvents: null),
                onDisabledDueToDiskWriteFailure: onDisabledDueToDiskWriteFailure,
                listenDiagnosticMessages: true)
        {
            Contract.RequiresNotNull(eventSource);
            Contract.RequiresNotNull(writer);
            m_loggingContext = loggingContext;
        }

        /// <summary>
        /// Registers an additional event source (a generated ETW event source)
        /// </summary>
        public override void RegisterEventSource(EventSource eventSource)
        {
            // StatisticsEventListener only needs to listen to the diagnostics events from Tracing.ETWLogger
            if (eventSource == BuildXL.Tracing.ETWLogger.Log)
            {
                base.RegisterEventSource(eventSource);
            }
        }

        /// <inheritdoc/>
        protected override void OnVerbose(EventWrittenEventArgs eventData)
        {
            // First we filter to events with the appropriate event id
            if (eventData.EventId == (int)LogEventId.Statistic ||
                eventData.EventId == (int)LogEventId.StatisticWithoutTelemetry)
            {
                Contract.AssertNotNull(eventData.Payload);

                var objKey = eventData.Payload[0] as string;
                var objValue = eventData.Payload[1];

                // BulkStatistic events should be normalized to have the standard statistic naming convention
                // when they go into the file
                var key = (objKey ?? string.Empty).Replace("BulkStatistic_", string.Empty);
                var value = objValue == null ? 0L : (long)objValue;

                if (ShouldSendStatisticToFinalStatistics(key))
                {
                    m_finalStatistics.AddOrUpdate(key, value, (k, v) => value);
                }

                Output(eventData.Level, eventData, string.Format(CultureInfo.InvariantCulture, "{0}={1}", key, value));
            }
        }

        /// <summary>
        /// Whether the statistic should be included in FinalStatistics
        /// </summary>
        public static bool ShouldSendStatisticToFinalStatistics(string statisticName)
        {
            // Some events are routed to the stats file for convenience but they're already represented in other
            // telemetry events. They go by the convention of EventName_EventProperty. We can use a remaining
            // underscore as a hit that the data is already represented in other events that we can omit from the
            // FinalStatistic event
            int firstUnderscore = statisticName.IndexOf('_');
            if (firstUnderscore != -1)
            {
                // The exception to this rule is when there is a statistic not already represented in another telemetry
                // event that still has a '_'. One example of this would be "SomeCatgory.Stat_Name_With_Underscores"
                int firstPeriod = statisticName.IndexOf('.', 0, firstUnderscore);
                return firstPeriod != -1;
            }

            return true;
        }

        /// <inheritdoc/>
        protected override void OnCritical(EventWrittenEventArgs eventData)
        {
        }

        /// <inheritdoc/>
        protected override void OnWarning(EventWrittenEventArgs eventData)
        {
        }

        /// <inheritdoc/>
        protected override void OnError(EventWrittenEventArgs eventData)
        {
        }

        /// <inheritdoc/>
        protected override void OnInformational(EventWrittenEventArgs eventData)
        {
        }

        /// <inheritdoc/>
        protected override void OnAlways(EventWrittenEventArgs eventData)
        {
        }

        /// <inheritdoc />
        protected override void OnSuppressedWarning(EventWrittenEventArgs eventData)
        {
        }

        /// <inheritdoc />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA1816:CallGCSuppressFinalizeCorrectly")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1063:ImplementIDisposableCorrectly")]
        public override void Dispose()
        {
            base.Dispose();
        }

        /// <summary>
        /// Sends the FinalStatistic event to telemetry
        /// </summary>
#pragma warning disable CA1822 // Member can be static (valid only for .Net core builds)
        public void SendFinalStatistics()
#pragma warning restore CA1822 // Member can be static
        {
            // Send a final telemetry event with all statistics accumulated during the build for ease of joining
            Logger.Log.FinalStatistics(m_loggingContext, m_finalStatistics);
        }
    }
}
