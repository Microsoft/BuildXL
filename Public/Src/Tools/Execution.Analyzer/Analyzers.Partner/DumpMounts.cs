﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.ToolSupport;
using Newtonsoft.Json;

namespace BuildXL.Execution.Analyzer
{
    internal partial class Args
    {
        public Analyzer InitializeDumpMountsAnalyzer()
        {
            string outputFilePath = null;

            foreach (var opt in AnalyzerOptions)
            {
                if (opt.Name.Equals("outputFile", StringComparison.OrdinalIgnoreCase) ||
                    opt.Name.Equals("o", StringComparison.OrdinalIgnoreCase))
                {
                    outputFilePath = ParseSingletonPathOption(opt, outputFilePath);
                }
                else
                {
                    throw Error("Unknown option for dump mounts analysis: {0}", opt.Name);
                }
            }

            if (string.IsNullOrEmpty(outputFilePath))
            {
                throw Error("/outputFile parameter is required");
            }

            return new DumpMountsAnalyzer(GetAnalysisInput(), outputFilePath);
        }

        private static void WriteDumpMountsAnalyzerHelp(HelpWriter writer)
        {
            writer.WriteBanner("Dump Mounts Analysis");
            writer.WriteModeOption(nameof(AnalysisMode.DumpMounts), "Generates a JSON file containing information about all the mounts");
            writer.WriteOption("outputFile", "Required. The location of the output file.", shortName: "o");
        }
    }

    internal class DumpMountsAnalyzer : Analyzer
    {
        private readonly string m_outputFilePath;

        public DumpMountsAnalyzer(AnalysisInput input, string outputFilePath) : base(input)
        {
            m_outputFilePath = outputFilePath;
        }

        public override int Analyze()
        {
            var pathTable = CachedGraph.Context.PathTable;
            var mounts = CachedGraph.MountPathExpander.GetAllMountsByName();

            using (var writer = new JsonTextWriter(File.CreateText(m_outputFilePath)))
            {
                writer.Formatting = Formatting.Indented;

                writer.WriteStartObject();

                foreach (var mount in mounts)
                {
                    Contract.Assert(mount.Value.IsValid);

                    writer.WritePropertyName(mount.Key);
                    {
                        writer.WriteStartObject();

                        writer.WritePropertyName("Root");
                        writer.WriteValue(mount.Value.Root.ToString(pathTable));

                        writer.WritePropertyName("Flags");
                        {
                            writer.WriteStartObject();

                            writer.WritePropertyName("AllowHashing");
                            writer.WriteValue(mount.Value.AllowHashing);

                            writer.WritePropertyName("IsReadable");
                            writer.WriteValue(mount.Value.IsReadable);

                            writer.WritePropertyName("IsWritable");
                            writer.WriteValue(mount.Value.IsWritable);

                            writer.WritePropertyName("AllowCreateDirectory");
                            writer.WriteValue(mount.Value.AllowCreateDirectory);

                            writer.WritePropertyName("IsSystem");
                            writer.WriteValue(mount.Value.IsSystem);

                            writer.WritePropertyName("IsStatic");
                            writer.WriteValue(mount.Value.IsStatic);

                            writer.WritePropertyName("IsScrubbable");
                            writer.WriteValue(mount.Value.IsScrubbable);

                            writer.WritePropertyName("HasPotentialBuildOutputs");
                            writer.WriteValue(mount.Value.HasPotentialBuildOutputs);

                            writer.WriteEndObject();
                        }

                        writer.WriteEndObject();
                    }
                }

                writer.WriteEndObject();
            }

            return 0;
        }
    }
}
