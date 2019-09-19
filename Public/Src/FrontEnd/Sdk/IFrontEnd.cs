// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using BuildXL.Utilities.Configuration;
using JetBrains.Annotations;

namespace BuildXL.FrontEnd.Sdk
{
    /// <summary>
    /// A front end is able to create resolvers for a set of supported resolver kinds.
    /// </summary>
    [SuppressMessage("Microsoft.Naming", "CA1724:TypeNamesShouldNotMatchNamespaces")]
    public interface IFrontEnd
    {
        /// <summary>
        /// Returns the supported resolvers
        /// </summary>
        /// <returns>The resulting collection is not null or empty.</returns>
        [JetBrains.Annotations.NotNull]
        IReadOnlyCollection<string> SupportedResolvers { get; }

        /// <summary>
        /// Initializes the frontend
        /// </summary>
        void InitializeFrontEnd([JetBrains.Annotations.NotNull]FrontEndHost host, [JetBrains.Annotations.NotNull]FrontEndContext context, [JetBrains.Annotations.NotNull]IConfiguration frontEndConfiguration);

        /// <summary>
        /// Creates a resolver for a given kind. The resolver must be part of the front end
        /// supported resolvers.
        /// </summary>
        [JetBrains.Annotations.NotNull]
        IResolver CreateResolver([JetBrains.Annotations.NotNull]string kind);

        /// <summary>
        /// Allows a frontend to log its statistics after evaluation
        /// </summary>
        void LogStatistics(Dictionary<string, long> statistics);
    }
}
