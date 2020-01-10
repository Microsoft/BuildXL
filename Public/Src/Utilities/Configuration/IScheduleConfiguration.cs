// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Interop.MacOS;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Defines a named root
    /// </summary>
    public partial interface IScheduleConfiguration
    {
        /// <summary>
        /// Specifies the maximum amount of RAM which can be utilized before scheduling is paused to allow freeing resources.
        /// NOTE: In order for scheduling to be paused, both this limit and <see cref="MinimumTotalAvailableRamMb"/> must be met.
        /// </summary>
        int MaximumRamUtilizationPercentage { get; }

        /// <summary>
        /// Specifies the minimum amount of available RAM before scheduling is paused to allow freeing resources.
        /// NOTE: In order for scheduling to be paused, both this limit and <see cref="MaximumRamUtilizationPercentage"/> must be met.
        /// </summary>
        int MinimumTotalAvailableRamMb { get; }

        /// <summary>
        /// Indicates that processes should not be cancelled and retried when machine RAM is low as specified by
        /// <see cref="MaximumRamUtilizationPercentage"/> and <see cref="MinimumTotalAvailableRamMb"/>
        /// </summary>
        bool DisableProcessRetryOnResourceExhaustion { get; }

        /// <summary>
        /// Specifies the maximum allowed memory pressure level on macOS before scheduling is paused to allow freeing resources.
        /// </summary>
        Memory.PressureLevel MaximumAllowedMemoryPressureLevel { get; }

        /// <summary>
        /// Stops the build engine the first time an error is generated by either BuildXL or one of the tool it runs.
        /// </summary>
        bool StopOnFirstError { get; }

        /// <summary>
        /// Specifies the maximum number of concurrent worker selections for pip execution.
        /// Default is 1 for single machine build and 5 for distributed build
        /// </summary>
        int MaxChooseWorkerCpu { get; }

        /// <summary>
        /// Specifies the maximum number of concurrent worker selections for cachelookup.
        /// Default is 1.
        /// </summary>
        int MaxChooseWorkerCacheLookup { get; }

        /// <summary>
        /// Specifies the maximum number of processes that BuildXL will launch at one time. The default value is 25% more than the total number of processors in the current machine.
        /// </summary>
        int MaxProcesses { get; }

        /// <summary>
        /// Specifies the maximum number of cache lookups that can be concurrently done.
        /// </summary>
        int MaxCacheLookup { get; }

        /// <summary>
        /// Specifies the maximum number of processes that do materialize (e.g., materialize inputs, storing two-phase cache entries, analyzing pip violations).
        /// </summary>
        int MaxMaterialize { get; }

        /// <summary>
        /// Desired size of the light process queue.  Processes that have Process.Option.IsLight set (indicating
        /// that they are neither CPU nor IO bound; rather, for example, they more like lazy lingering observers)
        /// are placed in a special queue which has a much bigger capacity than the CpuQueue (which is meant for
        /// CPU intensive processes).
        /// </summary>
        int MaxLightProcesses { get; }

        /// <summary>
        /// Specifies the maximum number of I/O operations that BuildXL will launch at one time. The default value is 1/4 of the number of processors in the current machine, but at least 1.
        /// </summary>
        int MaxIO { get; }

        /// <summary>
        /// Adaptive IO limit
        /// </summary>
        bool AdaptiveIO { get; }

        /// <summary>
        /// Runs the build engine and all tools at a lower priority in order to provide better responsiveness to interactive processes on the current machine.
        /// </summary>
        bool LowPriority { get; }

        /// <summary>
        /// Enables lazy materialization (deployment) of pips' outputs from local cache. Defaults to on.
        /// </summary>
        /// <remarks>
        /// Previous internal name: EnableLazyOutputMaterialization
        /// </remarks>
        bool EnableLazyOutputMaterialization { get; }

        /// <summary>
        /// Forces skipping dependencies of explicitly scheduled pips unless inputs are non-existent on filesystem
        /// </summary>
        /// <remarks>
        /// Internal name: Dirty Build
        /// </remarks>
        ForceSkipDependenciesMode ForceSkipDependencies { get; }

        /// <summary>
        /// Flag for maintaining (reading and writing) historical performance information; future build schedules will improve by leveraging the collected information.
        /// </summary>
        bool UseHistoricalPerformanceInfo { get; }

        /// <summary>
        /// Ensures historic performance information is loaded from cache
        /// </summary>
        bool ForceUseEngineInfoFromCache { get; }

        /// <summary>
        /// Indicates whether historic perf information should be used to speculatively limit the RAM utilization
        /// of launched processes
        /// </summary>
        bool? UseHistoricalRamUsageInfo { get; }

        /// <summary>
        /// Specifies the set of outputs which must be materialized
        /// </summary>
        RequiredOutputMaterialization RequiredOutputMaterialization { get; }

        /// <summary>
        /// Gets set of excluded paths for output file materialization
        /// </summary>
        IReadOnlyList<AbsolutePath> OutputMaterializationExclusionRoots { get; }

        /// <summary>
        /// Treats directory as absent file when getting a content hash of inputs
        /// </summary>
        /// <remarks>
        /// This flag is temporary to avoid breaking changes caused by a fix for
        /// Bug #698382
        /// </remarks>
        bool TreatDirectoryAsAbsentFileOnHashingInputContent { get; }

        /// <summary>
        /// Allow copying symlink.
        /// </summary>
        bool AllowCopySymlink { get; }

        /// <summary>
        /// Reuse output files on disk during cache lookup (to check up-to-dateness) and materialization.
        /// </summary>
        /// <remarks>
        /// The up-to-dateness checks are done by querying the USN journal.
        /// </remarks>
        bool ReuseOutputsOnDisk { get; }

        /// <summary>
        /// Unsafe configuration that allows for disabling pip graph post validation.
        /// </summary>
        /// <remarks>
        /// TODO: Remove this!
        /// </remarks>
        bool UnsafeDisableGraphPostValidation { get; }

        /// <summary>
        /// String used to generate environment specific fingerprint for scheduler performance data (this is automatically computed)
        /// </summary>
        string EnvironmentFingerprint { get; }

        /// <summary>
        /// Verifies pins for cache lookup output content by attempting to materialize the content.
        /// </summary>
        bool VerifyCacheLookupPin { get; }

        /// <summary>
        /// Indicates whether outputs of cached pips should be pinned. (Defaults to true)
        /// </summary>
        bool PinCachedOutputs { get; }

        /// <summary>
        /// Canonicalize filter outputs.
        /// </summary>
        bool CanonicalizeFilterOutputs { get; }

        /// <summary>
        /// Schedule meta pips
        /// </summary>
        bool ScheduleMetaPips { get; }

        /// <summary>
        /// Number of retries for processes that users can specify.
        /// </summary>
        /// <remarks>
        /// One use of this process retry is when a process specifies some exit codes that allow it to be retried.
        /// </remarks>
        int ProcessRetries { get; }

        /// <summary>
        /// Create symlink lazily from the symlink definition manifest.
        /// </summary>
        bool UnsafeLazySymlinkCreation { get; }

        /// <summary>
        /// Enables lazy materialization of write file outputs. Defaults to off (on for CloudBuild)
        /// </summary>
        /// <remarks>
        /// TODO: This should be removed when lazy write file materialization works appropriately with copy pips AND incremental scheduling.
        /// </remarks>
        bool EnableLazyWriteFileMaterialization { get; }

        /// <summary>
        /// Gets whether IPC pip output should be written to disk. Defaults to on (off for CloudBuild)
        /// </summary>
        bool WriteIpcOutput { get; }

        /// <summary>
        /// Gets the mode for reporting unexpected symlink accesses which defines when unexpected accesses are reported
        /// </summary>
        UnexpectedSymlinkAccessReportingMode UnexpectedSymlinkAccessReportingMode { get; }

        /// <summary>
        /// Stores pip outputs to cache.
        /// </summary>
        bool StoreOutputsToCache { get; }

        /// <summary>
        /// Infers the non-existence of a path based on the parent path when checking the real file system in file system view.
        /// </summary>
        bool InferNonExistenceBasedOnParentPathInRealFileSystem { get; }

        /// <summary>
        /// Enables incremental scheduling to schedule fewer pips.
        /// </summary>
        /// <remarks>
        /// This implies <see cref="IEngineConfiguration.ScanChangeJournal" /> and is functionally a superset.
        /// </remarks>
        bool IncrementalScheduling { get; }

        /// <summary>
        /// Computes static fingerprints of pips during graph construction.
        /// </summary>
        /// <remarks>
        /// This option is enabled when <see cref="IncrementalScheduling"/> is set to true.
        /// On Word build, the graph construction is 10%-13% slower when static fingerprints are computed.
        /// In the future BuildXL may want to use this static fingerprints to compute weak content fingerprints, and thus this option
        /// can be deprecated.
        /// </remarks>
        bool ComputePipStaticFingerprints { get; }

        /// <summary>
        /// Logs static fingerprints of pips during graph construction.
        /// </summary>
        /// <remarks>
        /// This option is useful for debugging graph agnostic incremental scheduling state.
        /// </remarks>
        bool LogPipStaticFingerprintTexts { get; }

        /// <summary>
        /// Creates handle with sequential scan when hashing output files specified in <see cref="OutputFileExtensionsForSequentialScanHandleOnHashing"/>.
        /// </summary>
        /// <remarks>
        /// Currently, this option will only have effect if <see cref="StoreOutputsToCache"/> is disabled.
        /// </remarks>
        bool CreateHandleWithSequentialScanOnHashingOutputFiles { get; }

        /// <summary>
        /// File extensions of outputs files which BuildXL will create handles with sequential scan when hashing the files.
        /// </summary>
        IReadOnlyList<PathAtom> OutputFileExtensionsForSequentialScanHandleOnHashing { get; }

        /// <summary>
        /// Prefix of tag considered for sending aggregate statistics to telemetry.
        /// </summary>
        string TelemetryTagPrefix { get; }

        /// <summary>
        /// Specifies the cpu queue limit in terms of a multiplier of the normal limit when at least one remote worker gets connected.
        /// </summary>
        double? MasterCpuMultiplier { get; }

        /// <summary>
        /// Specifies the cachelookup queue limit in terms of a multiplier of the normal limit when at least one remote worker gets connected.
        /// </summary>
        double? MasterCacheLookupMultiplier { get; }

        /// <summary>
        /// Skip hash source file pips during graph creation.
        /// </summary>
        bool SkipHashSourceFile { get; }

        /// <summary>
        /// Unsafe configuration that stops the shared opaque scrubber from deleting empty directories
        /// </summary>
        /// <remarks>
        /// The reason this flag is unsafe is because not deleting empty directories may introduce
        /// nondeterminism to a build; shared opaques should be wiped-clean of non-build files before
        /// engine execution.
        ///
        /// For example, if ./a is a shared opaque, ./a/b is an undeclared empty directory, and some.exe
        /// fails if ./a/b does not exist, then this flag will allow some.exe to execute successfully
        /// even though it normally would not.
        ///
        /// TODO: Remove this when https://gitlab.kitware.com/cmake/cmake/issues/19162 has reached mainstream versions
        /// </remarks>
        bool UnsafeDisableSharedOpaqueEmptyDirectoryScrubbing { get; }

        /// <summary>
        /// Delay scrubbing of shared opaque outputs until right before the pip is executed.
        /// 
        /// It's currently unsafe because not all corner cases have been worked out.
        /// </summary>
        bool UnsafeLazySODeletion { get; }

        /// <summary>
        /// Indicates whether historic cpu information should be used to decide the weight of process pips.
        /// </summary>
        bool UseHistoricalCpuUsageInfo { get; }

        /// <summary>
        /// Instead of creating a random moniker for API server, use a fixed predetermined moniker.
        /// </summary>
        bool UseFixedApiServerMoniker { get; }

        /// <summary>
        /// Path to file containing input changes.
        /// </summary>
        AbsolutePath InputChanges { get; }

        /// <summary>
        /// Required minimum available disk space on all drives to keep executing pips 
        /// Checked every 2 seconds.
        /// </summary>
        int? MinimumDiskSpaceForPipsGb { get; }

        /// <summary>
        /// Instructs the scheduler to only perform cache lookup and skip execution of pips that are cache misses.
        /// </summary>
        bool CacheOnly { get; }
    }
}
