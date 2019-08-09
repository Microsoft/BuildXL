﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Pips.Operations;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.Analyzer.Analyzers
{
    /// <summary>
    /// Analyzer for generating graph fragment.
    /// </summary>
    public class GraphFragmentGenerator : Analyzer
    {
        private string m_outputFile;
        private string m_description;
        private readonly OptionName m_outputFileOption = new OptionName("OutputFile", "o");
        private readonly OptionName m_descriptionOption = new OptionName("Description", "d");

        private AbsolutePath m_absoluteOutputPath;

        /// <inheritdoc />
        public override AnalyzerKind Kind => AnalyzerKind.GraphFragment;

        /// <inheritdoc />
        public override EnginePhases RequiredPhases => EnginePhases.Schedule;

        /// <inheritdoc />
        public override bool HandleOption(CommandLineUtilities.Option opt)
        {
            if (string.Equals(m_outputFileOption.LongName, opt.Name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(m_outputFileOption.ShortName, opt.Name, StringComparison.OrdinalIgnoreCase))
            {
                m_outputFile = CommandLineUtilities.ParsePathOption(opt);
                return true;
            }

            if (string.Equals(m_descriptionOption.LongName, opt.Name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(m_descriptionOption.ShortName, opt.Name, StringComparison.OrdinalIgnoreCase))
            {
                m_description = opt.Value;
                return true;
            }

            return base.HandleOption(opt);
        }

        /// <inheritdoc />
        public override void WriteHelp(HelpWriter writer)
        {
            writer.WriteOption(m_outputFileOption.LongName, "The path where the graph fragment should be generated", shortName: m_outputFileOption.ShortName);
            base.WriteHelp(writer);
        }

        /// <inheritdoc />
        public override bool Initialize()
        {
            if (string.IsNullOrEmpty(m_outputFile))
            {
                Logger.GraphFragmentMissingOutputFile(LoggingContext, m_outputFileOption.LongName);
                return false;
            }

            if (!Path.IsPathRooted(m_outputFile))
            {
                m_outputFile = Path.GetFullPath(m_outputFile);
            }

            if (!AbsolutePath.TryCreate(PathTable, m_outputFile, out m_absoluteOutputPath))
            {
                Logger.GraphFragmentInvalidOutputFile(LoggingContext, m_outputFile, m_outputFileOption.LongName);
                return false;
            }

            return base.Initialize();
        }

        /// <inheritdoc />
        public override bool AnalyzeSourceFile(BuildXL.FrontEnd.Workspaces.Core.Workspace workspace, AbsolutePath path, ISourceFile sourceFile) => true;

        /// <inheritdoc />
        public override bool FinalizeAnalysis()
        {
            if (PipGraph == null)
            {
                Logger.GraphFragmentMissingGraph(LoggingContext);
                return false;
            }

            var serializer = new PipGraphFragmentSerializer(Context, new PipGraphFragmentContext());

            try
            {
                var pips = PipGraph.RetrieveScheduledPips().ToList();
                var finalPipList = TopSort(pips);
                finalPipList = StableSortPips(pips, finalPipList);
                serializer.Serialize(m_absoluteOutputPath, finalPipList, pips.Count, m_description);
            }
            catch (Exception e) when (e is BuildXLException || e is IOException)
            {
                Logger.GraphFragmentExceptionOnSerializingFragment(LoggingContext, m_absoluteOutputPath.ToString(Context.PathTable), e.ToString());
                return false;
            }

            return base.FinalizeAnalysis();
        }

        /// <summary>
        /// The pips should be in a similar order to how they were originally inserted into the graph
        /// </summary>
        private static List<List<Pip>> StableSortPips(List<Pip> pips, List<List<Pip>> finalPipList)
        {
            Dictionary<Pip, int> order = new Dictionary<Pip, int>();
            for (int i = 0; i < pips.Count; i++)
            {
                order[pips[i]] = i;
            }

            finalPipList = finalPipList.Select(pipGroup => pipGroup.OrderBy(pip => order[pip]).ToList()).ToList();
            return finalPipList;
        }

        private List<List<Pip>> TopSort(List<Pip> pips)
        {
            Dictionary<Pip, int> childrenLeftToVisit = new Dictionary<Pip, int>();
            List<List<Pip>> finalPipList = new List<List<Pip>>();

            finalPipList.Add(new List<Pip>());
            int totalAdded = 0;
            foreach (var pip in pips)
            {
                childrenLeftToVisit[pip] = 0;
            }
            foreach (var pip in pips)
            {
                foreach (var dependent in (PipGraph.RetrievePipImmediateDependents(pip) ?? Enumerable.Empty<Pip>()))
                {
                    childrenLeftToVisit[dependent]++;
                }
            }

            foreach (var pip in pips)
            {
                if (childrenLeftToVisit[pip] == 0)
                {
                    totalAdded++;
                    finalPipList[0].Add(pip);
                }
            }

            int currentLevel = 0;
            while (totalAdded < pips.Count)
            {
                finalPipList.Add(new List<Pip>());
                foreach (var pip in finalPipList[currentLevel])
                {
                    foreach (var dependent in PipGraph.RetrievePipImmediateDependents(pip) ?? Enumerable.Empty<Pip>())
                    {
                        if (--childrenLeftToVisit[dependent] == 0)
                        {
                            totalAdded++;
                            finalPipList[currentLevel + 1].Add(dependent);
                        }
                    }
                }

                currentLevel++;
            }

            return finalPipList;
        }

        private struct OptionName
        {
            public readonly string LongName;
            public readonly string ShortName;

            public OptionName(string name)
            {
                LongName = name;
                ShortName = null;
            }

            public OptionName(string longName, string shortName)
            {
                LongName = longName;
                ShortName = shortName;
            }
        }
    }
}
