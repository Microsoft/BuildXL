﻿using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using Kusto.Data.Common;

namespace BuildXL.Cache.Monitor.App.Rules
{
    internal class LastRestoredCheckpointRule : KustoRuleBase
    {
        public class Configuration : KustoRuleConfiguration
        {
            public Configuration(KustoRuleConfiguration kustoRuleConfiguration)
                : base(kustoRuleConfiguration)
            {
            }

            public TimeSpan LookbackPeriod { get; set; } = TimeSpan.FromDays(1);

            public TimeSpan ActivityThreshold { get; set; } = TimeSpan.FromHours(1);

            public int FatalMissingMachinesThreshold { get; set; } = 20;

            public TimeSpan ErrorThreshold { get; set; } = TimeSpan.FromMinutes(45);
        }

        private readonly Configuration _configuration;

        public override string Identifier => $"{nameof(LastRestoredCheckpointRule)}:{_configuration.Environment}/{_configuration.Stamp}";

        public LastRestoredCheckpointRule(Configuration configuration)
            : base(configuration)
        {
            Contract.RequiresNotNull(configuration);
            _configuration = configuration;
        }

#pragma warning disable CS0649
        private class Result
        {
            public string Machine;
            public DateTime? LastRestoreTime;
        }
#pragma warning restore CS0649

        public override async Task Run(RuleContext context)
        {
            var query =
                $@"
                let Events = CloudBuildLogEvent
                | where PreciseTimeStamp > ago({CslTimeSpanLiteral.AsCslString(_configuration.LookbackPeriod)})
                | where Stamp == ""{_configuration.Stamp}""
                | where Service == ""{Constants.ServiceName}"";
                let Machines = Events
                | summarize LastActivityTime=max(PreciseTimeStamp) by Machine
                | where LastActivityTime >= ago({CslTimeSpanLiteral.AsCslString(_configuration.ActivityThreshold)})
                | project-away LastActivityTime;
                let Restores = Events
                | where Message has ""RestoreCheckpointAsync stop""
                | summarize LastRestoreTime=max(PreciseTimeStamp) by Machine;
                Machines
                | join hint.strategy=broadcast kind=leftouter Restores on Machine
                | project-away Machine1
                | where not(isnull(Machine))";
            var results = (await QuerySingleResultSetAsync<Result>(query)).ToList();

            var now = _configuration.Clock.UtcNow;
            if (results.Count == 0)
            {
                Emit(context, "NoLogs", Severity.Fatal,
                    $"No machines logged anything in the last day");
                return;
            }

            var missing = new List<string>();
            var failures = new List<Tuple<string, TimeSpan>>();
            foreach (var result in results)
            {
                if (!result.LastRestoreTime.HasValue)
                {
                    missing.Add(result.Machine);
                    continue;
                }

                var age = now - result.LastRestoreTime.Value;
                if (age >= _configuration.ErrorThreshold)
                {
                    failures.Add(new Tuple<string, TimeSpan>(result.Machine, age));
                }
            }

            if (missing.Count > 0)
            {
                var severity = missing.Count < _configuration.FatalMissingMachinesThreshold ? Severity.Error : Severity.Fatal;
                var formattedMissing = missing.Select(m => $"`{m}`");
                var machinesCsv = string.Join(", ", formattedMissing);
                var shortMachinesCsv = string.Join(", ", formattedMissing.Take(5));
                Emit(context, "NoRestoresThreshold", severity,
                    $"Found {missing.Count} machine(s) active in the last `{_configuration.ActivityThreshold}`, but without checkpoints restored in at least `{_configuration.LookbackPeriod}`: {machinesCsv}",
                    $"`{missing.Count}` machine(s) haven't restored checkpoints in at least `{_configuration.LookbackPeriod}`. Examples: {shortMachinesCsv}");
            }

            if (failures.Count > 0)
            {
                var formattedFailures = failures.Select(f => $"`{f.Item1}` ({f.Item2})");
                var machinesCsv = string.Join(", ", formattedFailures);
                var shortMachinesCsv = string.Join(", ", formattedFailures.Take(5));
                Emit(context, "OldRestores", Severity.Error,
                    $"Found `{failures.Count}` machine(s) active in the last `{_configuration.ActivityThreshold}`, but with old checkpoints (at least `{_configuration.ErrorThreshold}`): {machinesCsv}",
                    $"`{failures.Count}` machine(s) have checkpoints older than `{_configuration.ErrorThreshold}`. Examples: {shortMachinesCsv}");
            }
        }
    }
}
