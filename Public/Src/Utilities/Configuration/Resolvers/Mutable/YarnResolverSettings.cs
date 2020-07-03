// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <summary>
    /// Resolver settings for the Yarn front-end.
    /// </summary>
    public class YarnResolverSettings : JavaScriptResolverSettings, IYarnResolverSettings
    {
        /// <nodoc/>
        public YarnResolverSettings()
        {
        }

        /// <nodoc/>
        public YarnResolverSettings(IYarnResolverSettings resolverSettings, PathRemapper pathRemapper) : base(resolverSettings, pathRemapper)
        {
            YarnLocation = resolverSettings.YarnLocation.HasValue? (FileArtifact?) pathRemapper.Remap(resolverSettings.YarnLocation.Value) : null;
        }

        /// <inheritdoc/>
        public FileArtifact? YarnLocation { get; set; }
    }
}
