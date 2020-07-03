// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <summary>
    /// Resolver settings for the MSBuild front-end.
    /// </summary>
    public class MsBuildResolverSettings : ResolverSettings, IMsBuildResolverSettings
    {
        private AbsolutePath m_rootTraversal;

        /// <nodoc/>
        public MsBuildResolverSettings()
        {
            // We allow the source directory to be writable by default
            AllowWritableSourceDirectory = true;
            // We want changes under program files to be tracked
        }

        /// <nodoc/>
        public MsBuildResolverSettings(
            IMsBuildResolverSettings resolverSettings,
            PathRemapper pathRemapper)
            : base(resolverSettings, pathRemapper)
        {
            Root = pathRemapper.Remap(resolverSettings.Root);
            RootTraversal = pathRemapper.Remap(resolverSettings.RootTraversal);
            ModuleName = resolverSettings.ModuleName;
            AdditionalOutputDirectories = resolverSettings.AdditionalOutputDirectories;
            UntrackedDirectoryScopes = resolverSettings.UntrackedDirectoryScopes;
            UntrackedFiles = resolverSettings.UntrackedFiles;
            UntrackedDirectories = resolverSettings.UntrackedDirectories;
            RunInContainer = resolverSettings.RunInContainer;
            MsBuildSearchLocations = resolverSettings.MsBuildSearchLocations;
            FileNameEntryPoints = resolverSettings.FileNameEntryPoints;
            InitialTargets = resolverSettings.InitialTargets;
            Environment = resolverSettings.Environment;
            GlobalProperties = resolverSettings.GlobalProperties;
            LogVerbosity = resolverSettings.LogVerbosity;
            EnableBinLogTracing = resolverSettings.EnableBinLogTracing;
            EnableEngineTracing = resolverSettings.EnableEngineTracing;
            KeepProjectGraphFile = resolverSettings.KeepProjectGraphFile;
            EnableTransitiveProjectReferences = resolverSettings.EnableTransitiveProjectReferences;
            UseLegacyProjectIsolation = resolverSettings.UseLegacyProjectIsolation;
            DoubleWritePolicy = resolverSettings.DoubleWritePolicy;
            AllowProjectsToNotSpecifyTargetProtocol = resolverSettings.AllowProjectsToNotSpecifyTargetProtocol;
            MsBuildRuntime = resolverSettings.MsBuildRuntime;
            DotNetSearchLocations = resolverSettings.DotNetSearchLocations;
            UseManagedSharedCompilation = resolverSettings.UseManagedSharedCompilation;
        }

        /// <inheritdoc/>
        public AbsolutePath Root { get; set; }

        /// <inheritdoc/>
        public AbsolutePath RootTraversal
        {
            // If RootTraversal is not set, we use the value of Root
            get => m_rootTraversal != AbsolutePath.Invalid ? m_rootTraversal : Root;
            set { m_rootTraversal = value; }
        }

        /// <inheritdoc/>
        public string ModuleName { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<DirectoryArtifact> AdditionalOutputDirectories { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<DirectoryArtifact> UntrackedDirectoryScopes { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<FileArtifact> UntrackedFiles { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<DirectoryArtifact> UntrackedDirectories { get; set; }

        /// <inheritdoc/>
        public bool RunInContainer { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<DirectoryArtifact> MsBuildSearchLocations { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<RelativePath> FileNameEntryPoints { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<string> InitialTargets { get; set; }

        /// <inheritdoc/>
        public IReadOnlyDictionary<string, DiscriminatingUnion<string, UnitValue>> Environment { get; set; }

        /// <inheritdoc/>
        public IReadOnlyDictionary<string, string> GlobalProperties { get; set; }

        /// <inheritdoc/>
        public string LogVerbosity { get; set; }

        /// <inheritdoc/>
        public bool? EnableBinLogTracing { get; set; }

        /// <inheritdoc/>
        public bool? EnableEngineTracing { get; set; }

        /// <inheritdoc/>
        public bool? KeepProjectGraphFile { get; set; }

        /// <inheritdoc/>
        public bool? EnableTransitiveProjectReferences { get; set; }

        /// <inheritdoc/>
        public bool? UseLegacyProjectIsolation { get; set; }

        /// <inheritdoc/>
        public DoubleWritePolicy? DoubleWritePolicy { get; set; }

        /// <inheritdoc/>
        public bool? AllowProjectsToNotSpecifyTargetProtocol { get; set; }

        /// <inheritdoc/>
        public string MsBuildRuntime { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<DirectoryArtifact> DotNetSearchLocations { get; set; }

        /// <inheritdoc/>
        public bool? UseManagedSharedCompilation { get; set; }
    }
}
