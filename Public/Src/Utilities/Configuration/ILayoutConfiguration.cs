// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Configures how things are laid out.
    /// </summary>
    public partial interface ILayoutConfiguration
    {
        /// <summary>
        /// The primary config file
        /// </summary>
        AbsolutePath PrimaryConfigFile { get; }

        /// <summary>
        /// The output directory
        /// </summary>
        AbsolutePath SourceDirectory { get; }

        /// <summary>
        /// The output directory
        /// </summary>
        AbsolutePath OutputDirectory { get; }

        /// <summary>
        /// The object directory
        /// </summary>
        AbsolutePath ObjectDirectory { get; }

        /// <summary>
        /// When pips run in Helium containers, the root of the directories used to virtualize
        /// inputs and generate outputs.
        /// </summary>
        AbsolutePath RedirectedDirectory { get; }

        /// <summary>
        /// The frontend directory
        /// </summary>
        AbsolutePath FrontEndDirectory { get; }

        /// <summary>
        /// The Cache Directory
        /// </summary>
        AbsolutePath CacheDirectory { get; }

        /// <summary>
        /// The Engine Cache Directory
        /// </summary>
        AbsolutePath EngineCacheDirectory { get; }

        /// <summary>
        /// The temp directory
        /// </summary>
        AbsolutePath TempDirectory { get; }

        /// <summary>
        /// The absolute path of the folder where the BuildXL binaries that are currently being used are located.
        /// </summary>
        /// <remarks>
        /// Will always be set by the engine.
        /// </remarks>
        AbsolutePath BuildEngineDirectory { get; set; }

        /// <summary>
        /// Path to file containing known hashes for files.
        /// </summary>
        AbsolutePath FileContentTableFile { get; }

        /// <summary>
        /// File change tracker file.
        /// </summary>
        AbsolutePath SchedulerFileChangeTrackerFile { get; }

        /// <summary>
        /// Incremental scheduling state file.
        /// </summary>
        AbsolutePath IncrementalSchedulingStateFile { get; }

        /// <summary>
        /// The fingerprint store directory.
        /// </summary>
        AbsolutePath FingerprintStoreDirectory { get; }

        /// <summary>
        /// Where all writes to shared opaque directories will be logged as soon as they happen.
        /// </summary>
        AbsolutePath SharedOpaqueSidebandDirectory { get; }

        /// <summary>
        /// Indicates if the engine should emit a warning to let users know that Spotlight indexing on layout configuration
        /// directories could decrease build performance, on by default.
        /// </summary>
        bool EmitSpotlightIndexingWarning { get; }
        
        /// <summary>
        /// Indicates whether a user profile has been redirected
        /// </summary>
        AbsolutePath RedirectedUserProfileJunctionRoot { get; }

        /// <summary>
        /// Directory containing files used to communicate between BuildXL process and tools for external execution, e.g., VmCommandProxy.
        /// </summary>
        AbsolutePath ExternalSandboxedProcessDirectory { get; set; }
    }
}
