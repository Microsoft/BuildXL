// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.FrontEnd.Nuget;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Tracing;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Sdk;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Script
{
    /// <summary>
    /// Resolver for NuGet packages.
    /// </summary>
    public sealed class NugetResolver : DScriptSourceResolver
    {
        internal const string NugetResolverName = "ComponentGovernance";
        private WorkspaceNugetModuleResolver m_nugetWorkspaceResolver;

        /// <nodoc />
        public NugetResolver(
            FrontEndHost host,
            FrontEndContext context,
            IConfiguration configuration,
            IFrontEndStatistics statistics,
            SourceFileProcessingQueue<bool> parseQueue,
            Logger logger = null,
            IDecorator<EvaluationResult> evaluationDecorator = null)
            : base(host, context, configuration, statistics, parseQueue, logger, evaluationDecorator)
        { }

        /// <inheritdoc/>
        public override async Task<bool> InitResolverAsync(IResolverSettings resolverSettings, object workspaceResolver)
        {
            Contract.Requires(resolverSettings != null);

            Contract.Assert(m_resolverState == State.Created);
            Contract.Assert(
                resolverSettings is INugetResolverSettings,
                I($"Wrong type for resolver settings, expected {nameof(INugetResolverSettings)} but got {nameof(resolverSettings.GetType)}"));

            Name = resolverSettings.Name;
            m_resolverState = State.ResolverInitializing;

            m_nugetWorkspaceResolver = workspaceResolver as WorkspaceNugetModuleResolver;
            Contract.Assert(m_nugetWorkspaceResolver != null, "Workspace module resolver is expected to be of source type");

            // TODO: We could do something smarter in the future and just download/generate what is needed
            // Use this result to populate the dictionaries that are used for package retrieval (m_packageDirectories, m_packages and m_owningModules)
            var maybePackages = await m_nugetWorkspaceResolver.GetAllKnownPackagesAsync();

            if (!maybePackages.Succeeded)
            {
                // Error should have been reported.
                return false;
            }

            m_owningModules = new Dictionary<ModuleId, Package>();

            foreach (var package in maybePackages.Result.Values.SelectMany(v => v))
            {
                m_packages[package.Id] = package;
                m_owningModules[package.ModuleId] = package;
            }

            if (Configuration.FrontEnd.GenerateCgManifestForNugets.IsValid ||
                Configuration.FrontEnd.ValidateCgManifestForNugets.IsValid)
            {
                //System.Diagnostics.Debugger.Launch();
                var cgManfiestGenerator = new NugetCgManifestGenerator(Context);
                string generatedCgManifest = cgManfiestGenerator.GenerateCgManifestForPackages(maybePackages.Result);
                string existingCgManifest = "{}";

                if ( !Configuration.FrontEnd.GenerateCgManifestForNugets.IsValid &&
                      Configuration.FrontEnd.ValidateCgManifestForNugets.IsValid )
                {
                    // Validation of existing cgmainfest.json results in failure due to mismatch. Should fail the build in this case.
                    try
                    {
                        existingCgManifest = File.ReadAllText(Configuration.FrontEnd.ValidateCgManifestForNugets.ToString(Context.PathTable));
                        FrontEndHost.Engine.RecordFrontEndFile(
                            Configuration.FrontEnd.ValidateCgManifestForNugets,
                            NugetResolverName);
                    }
                    // CgManifest FileNotFound, log error and fail build
                    catch (DirectoryNotFoundException)
                    {
                        // TODO: Rijul Log (Make function if common log)
                        return false;
                    }
                    catch (FileNotFoundException)
                    {
                        // TODO: Rijul Log
                        return false;
                    }
                    if (!cgManfiestGenerator.CompareForEquality(generatedCgManifest, existingCgManifest))
                    {
                        // TODO: Rijul Log
                        return false;
                    }

                    m_resolverState = State.ResolverInitialized;
                    return true;
                }

                // GenerateCgManifestForNugets writes a new file, hence it will always be valid and does noty need validation

                try
                {
                    // We are calling FrontEndHost.Engine.RecordFrontEndFile towards the end of this function because we may update this file after the read below
                    // Updating the file will cause a hash mismatch and the build to fail if this file is read again downstream
                    existingCgManifest = File.ReadAllText(Configuration.FrontEnd.GenerateCgManifestForNugets.ToString(Context.PathTable));
                }
                // CgManifest FileNotFound, continue to write the new file
                // No operations required as the empty existingCgManifest will not match with the newly generated cgManifest
                catch (DirectoryNotFoundException) { }
                catch (FileNotFoundException) { }

                if (!cgManfiestGenerator.CompareForEquality(generatedCgManifest, existingCgManifest))
                {
                    

                    if (Configuration.FrontEnd.GenerateCgManifestForNugets.IsValid)
                    {
                        // Overwrite or create new cgmanifest.json file with updated nuget package and version info
                        string targetFilePath = Configuration.FrontEnd.GenerateCgManifestForNugets.ToString(Context.PathTable);

                        try
                        {
                            FileUtilities.CreateDirectory(Path.GetDirectoryName(targetFilePath));
                            await FileUtilities.WriteAllTextAsync(targetFilePath, generatedCgManifest, Encoding.UTF8);
                        }
                        catch (BuildXLException e)
                        {
                            throw new BuildXLException("Cannot write cgmanifest.json file to disk", e);
                        }
                    }
                }

                FrontEndHost.Engine.RecordFrontEndFile(
                    Configuration.FrontEnd.GenerateCgManifestForNugets,
                    NugetResolverName);
            }


            m_resolverState = State.ResolverInitialized;

            return true;
        }
    }
}
