// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Pips.Filter
{
    /// <summary>
    /// Empty pip filter that matches all pips
    /// </summary>
    public class EmptyFilter : PipFilter
    {
        /// <summary>
        /// Singleton instance of <see cref="EmptyFilter"/>.
        /// </summary>
        public static readonly EmptyFilter Instance = new EmptyFilter();

        /// <inheritdoc/>
        public override PipFilter Negate()
        {
            Contract.Assert(false, "The empty filter should never be negated.");
            throw new InvalidOperationException("The empty filter should never be negated.");
        }

        /// <inheritdoc/>
        public override void AddStatistics(ref FilterStatistics statistics)
        {
        }

        /// <inheritdoc/>
        protected override int GetDerivedSpecificHashCode()
        {
            return 0;
        }

        /// <inheritdoc/>
        public override bool CanonicallyEquals(PipFilter pipFilter)
        {
            return pipFilter is EmptyFilter;
        }

        /// <inheritdoc/>
        public override PipFilter Canonicalize(FilterCanonicalizer canonicalizer)
        {
            return canonicalizer.GetOrAdd(Instance);
        }

        /// <inheritdoc/>
        public override IReadOnlySet<FileOrDirectoryArtifact> FilterOutputsCore(IPipFilterContext context, bool negate = false, IList<PipId> constrainingPips = null)
        {
            Contract.Assert(false, "The empty filter should never be executed. It will always match so executing it is just wasted work.");
            throw new InvalidOperationException("The empty filter should never be executed. It will always match so executing it is just wasted work.");
        }
    }
}
