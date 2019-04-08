// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Linq;
using BuildXL.Utilities.CodeGenerationHelper;
using Microsoft.CodeAnalysis;
using EventGenerators = BuildXL.Utilities.Instrumentation.Common.Generators;

namespace BuildXL.LogGen
{
    /// <summary>
    /// Writes the generated loggers to implement the partial class
    /// </summary>
    internal sealed class LogWriter
    {
        private const string GlobalInstrumentationNamespace = "global::BuildXL.Utilities.Instrumentation.Common";
        private const string NotifyContextWhenErrorsAreLogged = "m_notifyContextWhenErrorsAreLogged";
        private const string NotifyContextWhenWarningsAreLogged = "m_notifyContextWhenWarningsAreLogged";

        private readonly string m_path;
        private readonly string m_namespace;
        private readonly string m_targetFramework;
        private readonly string m_targetRuntime;
        private readonly ErrorReport m_errorReport;

        private List<GeneratorBase> m_generators;

        /// <nodoc />
        public LogWriter(Configuration config, ErrorReport errorReport)
        {
            m_path = config.OutputCSharpFile;
            m_namespace = config.Namespace;
            m_errorReport = errorReport;
            m_targetFramework = config.TargetFramework;
            m_targetRuntime = config.TargetRuntime;
        }

        /// <summary>
        /// Writes the log file
        /// </summary>
        public int WriteLog(List<LoggingSite> loggingSites)
        {
            var itemsWritten = 0;
            using (var fs = File.Open(m_path, FileMode.Create))
            using (StreamWriter writer = new StreamWriter(fs))
            {
                CodeGenerator gen = new CodeGenerator((c) => writer.Write(c));
                gen.Ln("// <auto-generated/>\r\n");
                Dictionary<LoggingSite, List<GeneratorBase>> generatorMap = CreateGenerators(loggingSites, gen);
                foreach (IGrouping<INamedTypeSymbol, LoggingSite> typeAndSites in loggingSites.GroupBy(s => s.Method.ContainingType))
                {
                    itemsWritten++;
                    gen.Ln("namespace {0}", typeAndSites.Key.ContainingNamespace);
                    using (gen.Br)
                    {
                        // Figure out the namespaces needed
                        IDictionary<string, HashSet<string>> namespacesWithConditions = CombineNamespaces();
                        foreach (var namespacesWithCondition in namespacesWithConditions)
                        {
                            bool hasCondition = !string.IsNullOrEmpty(namespacesWithCondition.Key);
                            if (hasCondition)
                            {
                                gen.Ln("#if {0}", namespacesWithCondition.Key);
                            }
                            else
                            {
                                namespacesWithCondition.Value.Add(m_namespace);
                            }

                            foreach (var ns in namespacesWithCondition.Value)
                            {
                                gen.Ln("using {0};", ns);
                            }

                            if (hasCondition)
                            {
                                gen.Ln("#endif");
                            }
                        }

                        gen.Ln();
                        gen.Ln("#pragma warning disable 219");
                        gen.Ln();

                        gen.GenerateSummaryComment("Logging Instantiation");
                        gen.WriteGeneratedAttribute();
                        gen.Ln("{0} partial class {1} : {2}.LoggerBase", GetAccessibilityString(typeAndSites.Key.DeclaredAccessibility), typeAndSites.Key.Name, GlobalInstrumentationNamespace);
                        using (gen.Br)
                        {
                            gen.Ln("static private Logger m_log = new {0}Impl();", typeAndSites.Key.Name);
                            gen.Ln();

                            var notifyContextWhenErrorsAreLoggedIsUsed = false;
                            var notifyContextWhenWarningsAreLoggedIsUsed = false;

                            foreach (GeneratorBase generator in m_generators)
                            {
                                generator.GenerateAdditionalLoggerMembers();
                            }

                            gen.GenerateSummaryComment("Logging implementation");
                            gen.WriteGeneratedAttribute();
                            gen.Ln("private class {0}Impl: {0}", typeAndSites.Key.Name);
                            using (gen.Br)
                            {
                                foreach (LoggingSite site in typeAndSites)
                                {
                                    List<GeneratorBase> generators = generatorMap[site];

                                    gen.GenerateSummaryComment("Logging implementation");
                                    string methodParameters = string.Empty;
                                    if (site.Payload.Count > 0)
                                    {
                                        methodParameters = ", " + string.Join(", ", site.Payload.Select(i => i.Type.ToDisplayString() + " " + i.Name));
                                    }

                                    gen.Ln(
                                        "{0} override void {1}({2}.LoggingContext {3}{4})",
                                        GetAccessibilityString(site.Method.DeclaredAccessibility),
                                        site.Method.Name,
                                        GlobalInstrumentationNamespace,
                                        site.LoggingContextParameterName,
                                        methodParameters);
                                    using (gen.Br)
                                    {
                                        string methodArgs = string.Empty;
                                        if (site.Payload.Count > 0)
                                        {
                                            methodArgs = ", " + string.Join(", ", site.Payload.Select(i => i.Name));
                                        }

                                        if (site.Level == BuildXL.Utilities.Instrumentation.Common.Level.Critical)
                                        {
                                            // Critical events are always logged synchronously
                                            gen.Ln("{0}_Core({1}{2});",
                                                site.Method.Name,
                                                site.LoggingContextParameterName,
                                                methodArgs);
                                        }
                                        else
                                        {
                                            gen.Ln("if ({0}.{1})",
                                                site.LoggingContextParameterName,
                                                nameof(BuildXL.Utilities.Instrumentation.Common.LoggingContext.IsAsyncLoggingEnabled));
                                            using (gen.Br)
                                            {
                                                // NOTE: This allocates a closure for every log message when async logging is enabled.
                                                // This is assumed to not be non-issue as the logging infrastructure already has many allocations
                                                // as a part of logging so this allocation doesn't 
                                                gen.Ln("EnqueueLogAction({0}, {1}, () => {2}_Core({0}{3}));",
                                                    site.LoggingContextParameterName,
                                                    site.Id,
                                                    site.Method.Name,
                                                    methodArgs);
                                            }

                                            gen.Ln("else");
                                            using (gen.Br)
                                            {
                                                gen.Ln("{0}_Core({1}{2});",
                                                    site.Method.Name,
                                                    site.LoggingContextParameterName,
                                                    methodArgs);
                                            }
                                        }

                                        // Register errors on the logging context so code can assert that errors were logged
                                        if (site.Level == BuildXL.Utilities.Instrumentation.Common.Level.Error)
                                        {
                                            notifyContextWhenErrorsAreLoggedIsUsed = true;
                                            gen.Ln("if ({0})", NotifyContextWhenErrorsAreLogged);
                                            using (gen.Br)
                                            {
                                                gen.Ln("{0}.SpecifyErrorWasLogged({1});", site.LoggingContextParameterName, site.Id);
                                            }
                                        }

                                        // Register warnings on the logging context so code can assert that warnings were logged
                                        if (site.Level == BuildXL.Utilities.Instrumentation.Common.Level.Warning)
                                        {
                                            notifyContextWhenWarningsAreLoggedIsUsed = true;
                                            gen.Ln("if ({0})", NotifyContextWhenWarningsAreLogged);
                                            using (gen.Br)
                                            {
                                                gen.Ln("{0}.SpecifyWarningWasLogged();", site.LoggingContextParameterName);
                                            }
                                        }
                                    }

                                    gen.Ln();
                                    gen.Ln(
                                        "private void {0}_Core({1}.LoggingContext {2}{3})",
                                        site.Method.Name,
                                        GlobalInstrumentationNamespace,
                                        site.LoggingContextParameterName,
                                        methodParameters);
                                    using (gen.Br)
                                    {
                                        List<char> interceptedCode = new List<char>();
                                        using (gen.InterceptOutput((c) => interceptedCode.Add(c)))
                                        {
                                            foreach (GeneratorBase generator in generators)
                                            {
                                                generator.GenerateLogMethodBody(
                                                    site,
                                                    getMessageExpression: () =>
                                                    {
                                                        // Track whether the getMessage() function was called where there is a format string
                                                        if (site.GetMessageFormatParameters().Any())
                                                        {
                                                            // Only InspecMessage takes a fully constructed message.
                                                            // To avoid redundant allocations this callback creates
                                                            // an argument instead of creating a local variable.
                                                            return
                                                                string.Format(
                                                                    CultureInfo.InvariantCulture,
                                                                    "string.Format(System.Globalization.CultureInfo.InvariantCulture, \"{0}\", {1})",
                                                                    site.GetNormalizedMessageFormat(),
                                                                    string.Join(", ", site.GetMessageFormatParameters()));
                                                        }

                                                        return "\"" + site.SpecifiedMessageFormat + "\"";
                                                    });
                                            }
                                        }

                                        // Now we can write out the intercepted code from the code generators
                                        foreach (char c in interceptedCode)
                                        {
                                            gen.Output(c);
                                        }
                                    }

                                    gen.Ln();
                                }
                            }

                            if (notifyContextWhenErrorsAreLoggedIsUsed)
                            {
                                gen.Ln("private bool {0} = true;", NotifyContextWhenErrorsAreLogged);
                            }

                            if (notifyContextWhenWarningsAreLoggedIsUsed)
                            {
                                gen.Ln("private bool {0} = true;", NotifyContextWhenWarningsAreLogged);
                            }
                        }
                    }
                }

                foreach (GeneratorBase generator in m_generators)
                {
                    gen.Ln();
                    generator.GenerateClass();
                }
            }

            return itemsWritten;
        }

        /// <summary>
        /// Creates a mapping of logging site to the generators that must run for it
        /// </summary>
        private Dictionary<LoggingSite, List<GeneratorBase>> CreateGenerators(List<LoggingSite> loggingSites, CodeGenerator codeGenerator)
        {
            Dictionary<EventGenerators, GeneratorBase> generatorsByName = new Dictionary<EventGenerators, GeneratorBase>();
            Dictionary<LoggingSite, List<GeneratorBase>> generatorsBySite = new Dictionary<LoggingSite, List<GeneratorBase>>();

            foreach (LoggingSite site in loggingSites)
            {
                List<GeneratorBase> generators = new List<GeneratorBase>();

                foreach (EventGenerators gen in Enum.GetValues(typeof(EventGenerators)))
                {
                    if (gen == EventGenerators.None)
                    {
                        continue;
                    }

                    if ((site.EventGenerators & gen) != 0)
                    {
                        GeneratorBase generator;
                        if (generatorsByName.TryGetValue(gen, out generator))
                        {
                            generators.Add(generator);
                        }
                        else
                        {
                            Func<GeneratorBase> generatorFactory;
                            if (!Parser.SupportedGenerators.TryGetValue(gen, out generatorFactory))
                            {
                                // AriaV2Disabled is the only generator that's allow to be specified with not actual
                                // generator existing
                                Contract.Assert(gen == EventGenerators.AriaV2Disabled, "Failed to find a generator for " + gen.ToString() +
                                    ". This should have been caught in Parsing");
                                continue;
                            }

                            generator = generatorFactory();
                            generator.Initialize(m_namespace, m_targetFramework, m_targetRuntime, codeGenerator, loggingSites, m_errorReport);
                            generatorsByName.Add(gen, generator);
                            generators.Add(generator);
                        }
                    }
                }

                generatorsBySite[site] = generators;
            }

            m_generators = new List<GeneratorBase>(generatorsByName.Values);

            return generatorsBySite;
        }

        private IDictionary<string, HashSet<string>> CombineNamespaces()
        {
            IDictionary<string, HashSet<string>> namespacesWithConditions = new Dictionary<string, HashSet<string>>();

            foreach (GeneratorBase generator in m_generators)
            {
                foreach (var ns in generator.ConsumedNamespaces)
                {
                    HashSet<string> namespaces;
                    if (!namespacesWithConditions.TryGetValue(ns.Item2, out namespaces))
                    {
                        namespaces = new HashSet<string>();
                        namespacesWithConditions[ns.Item2] = namespaces;
                    }

                    namespaces.Add(ns.Item1);
                }
            }

            return namespacesWithConditions;
        }

        private string GetAccessibilityString(Accessibility accessibility)
        {
            switch (accessibility)
            {
                case Accessibility.Private:
                    return "private";
                case Accessibility.Protected:
                    return "protected";
                case Accessibility.Internal:
                    return "internal";
                case Accessibility.Public:
                    return "public";
                default:
                    m_errorReport.ReportError("Unsupported accessibility type: {0}", accessibility);
                    return null;
            }
        }
    }
}
