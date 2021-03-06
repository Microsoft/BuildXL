// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;

namespace BuildXL.Cache.ContentStore.Interfaces.FileSystem
{
    /// <summary>
    /// Defines data for placing in VFS
    /// </summary>
    public readonly struct VfsFilePlacementData
    {
        /// <summary>
        /// The content hash
        /// </summary>
        public readonly ContentHash Hash;

        /// <summary>
        /// The realization mode
        /// </summary>
        public readonly FileRealizationMode RealizationMode;

        /// <summary>
        /// The access mode
        /// </summary>
        public readonly FileAccessMode AccessMode;

        /// <nodoc />
        public VfsFilePlacementData(ContentHash hash, FileRealizationMode realizationMode, FileAccessMode accessMode)
        {
            Hash = hash;
            RealizationMode = realizationMode;
            AccessMode = accessMode;
        }
    }
}
