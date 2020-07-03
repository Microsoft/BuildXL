﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Storage.FileContentTableAccessor
{
    /// <summary>
    /// Factory of <see cref="IFileContentTableAccessor"/>.
    /// </summary>
    public static class FileContentTableAccessorFactory
    {
        /// <summary>
        /// Creates an instance of <see cref="IFileContentTableAccessor"/> based on the current OS.
        /// </summary>
        /// <param name="accessor">Output file content table accessor.</param>
        /// <param name="error">Error message for failed creation.</param>
        /// <returns>True if successful; otherwise false.</returns>
        public static bool TryCreate(out IFileContentTableAccessor accessor, out string error)
        {
            accessor = null;
            error = null;

            if (OperatingSystemHelper.IsUnixOS)
            {
                accessor = new FileContentTableAccessorUnix();
                return true;
            }

            VolumeMap volumeMap = VolumeMap.CreateMapOfAllLocalVolumes(new LoggingContext(nameof(IFileContentTableAccessor)));

            if (volumeMap == null)
            {
                error = "Failed to create volume map";
                return false;
            }

            accessor = new FileContentTableAccessorWin(volumeMap);
            return true;
        }
    }
}
