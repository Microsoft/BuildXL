// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;

namespace BuildXL.Cache.Host.Service.Internal
{
    /// <summary>
    /// Final settings object used for intializing a distributed cache instance
    /// </summary>
    public class ResolvedNamedCacheSettings
    {
        /// <summary>
        /// The name of the cache
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// The drive root of the cache
        /// </summary>
        public string Drive => Path.GetPathRoot(ResolvedCacheRootPath.Path);

        /// <summary>
        /// The specified settings for the cache
        /// </summary>
        public NamedCacheSettings Settings { get; }

        /// <summary>
        /// The full path to the cache root (with necessary data such as ScenarioName included)
        /// </summary>
        public AbsolutePath ResolvedCacheRootPath { get; }

        /// <summary>
        /// The distributed machine location used for interactive in a distributed cache environment
        /// </summary>
        public MachineLocation MachineLocation { get; }

        /// <nodoc />
        public ResolvedNamedCacheSettings(string name, NamedCacheSettings settings, AbsolutePath resolvedCacheRootPath, MachineLocation machineLocation)
        {
            Name = name;
            Settings = settings;
            ResolvedCacheRootPath = resolvedCacheRootPath;
            MachineLocation = machineLocation;
        }
    }
}
