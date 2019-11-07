﻿using System;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using Kusto.Data.Common;

namespace BuildXL.Cache.Monitor.App.Rules
{
    internal class LastProducedCheckpointRule : KustoRuleBase
    {
        public class Configuration : KustoRuleConfiguration
        {
            public Configuration(KustoRuleConfiguration kustoRuleConfiguration)
                : base(kustoRuleConfiguration)
            {
            }

            public TimeSpan LookbackPeriod { get; set; } = TimeSpan.FromHours(2);

            public TimeSpan WarningThreshold { get; set; } = TimeSpan.FromMinutes(30);

            public TimeSpan ErrorThreshold { get; set; } = TimeSpan.FromMinutes(45);
        }

        private readonly Configuration _configuration;

        public override string Identifier => $"{nameof(LastProducedCheckpointRule)}:{_configuration.Environment}/{_configuration.Stamp}";

        public LastProducedCheckpointRule(Configuration configuration)
            : base(configuration)
        {
            Contract.RequiresNotNull(configuration);
            _configuration = configuration;
        }

#pragma warning disable CS0649
        private class Result
        {
            public DateTime PreciseTimeStamp;
            public string Machine;
        }
#pragma warning restore CS0649

        public override async Task Run(RuleContext context)
        {
            // NOTE(jubayard): When a summarize is run over an empty result set, Kusto produces a single (null) row,
            // which is why we need to filter it out.
            var query =
                $@"CloudBuildLogEvent
                | where PreciseTimeStamp > ago({CslTimeSpanLiteral.AsCslString(_configuration.LookbackPeriod)})
                | where Stamp == ""{_configuration.Stamp}""
                | where Service == ""{Constants.MasterServiceName}""
                | where Message contains ""CreateCheckpointAsync stop""
                | summarize (PreciseTimeStamp, Machine)=arg_max(PreciseTimeStamp, Machine)
                | where not(isnull(PreciseTimeStamp))";
            var results = (await QuerySingleResultSetAsync<Result>(query)).ToList();

            var now = _configuration.Clock.UtcNow;
            if (results.Count == 0)
            {
                Emit(context, Severity.Fatal,
                    $"No checkpoints produced for at least {_configuration.LookbackPeriod}");
                return;
            }

            var age = now - results[0].PreciseTimeStamp;

            if (age >= _configuration.WarningThreshold)
            {
                var severity = Severity.Warning;
                var threshold = _configuration.WarningThreshold;
                if (age >= _configuration.ErrorThreshold)
                {
                    severity = Severity.Error;
                    threshold = _configuration.ErrorThreshold;
                }

                Emit(context, severity,
                    $"Newest checkpoint age `{age}` above threshold `{threshold}`. Master is {results[0].Machine}",
                    eventTimeUtc: results[0].PreciseTimeStamp);
            }
        }
    }
}
