// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Cache.Interfaces
{
    /// <summary>
    /// A struct that contains the cache statistics and the cache ID from
    /// which the statistics came.
    /// </summary>
    public readonly struct CacheSessionStatistics : System.IEquatable<CacheSessionStatistics>
    {
        /// <summary>
        /// The CacheId of the cache these statistics are about
        /// </summary>
        public readonly CacheId CacheId;

        /// <summary>
        /// A string representation of the underlying cache type
        /// </summary>
        /// <remarks>
        /// This can simply be the .Net type, or some other unique string to represent
        /// this cache implementation.
        /// </remarks>
        public readonly string CacheType;

        /// <summary>
        /// A dictionary of statistic names and values.  This
        /// </summary>
        public readonly Dictionary<string, double> Statistics;

        /// <summary>
        /// Basic constructor to make the read-only CacheSessionStatistics
        /// </summary>
        /// <param name="cacheId">CacheI of these statistics</param>
        /// <param name="statistics">The statistics dictionary</param>
        /// <param name="cacheType">The .Net type name for the cache.</param>
        public CacheSessionStatistics(CacheId cacheId, string cacheType, Dictionary<string, double> statistics)
        {
            Contract.Requires(cacheId != null);
            Contract.Requires(statistics != null);

            CacheId = cacheId;
            Statistics = statistics;
            CacheType = cacheType;
        }

        // Needed to keep FxCop happy

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return Statistics.GetHashCode();
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return (obj is CacheSessionStatistics) && Equals((CacheSessionStatistics)obj);
        }

        /// <nodoc />
        bool System.IEquatable<CacheSessionStatistics>.Equals(CacheSessionStatistics other)
        {
            return object.ReferenceEquals(Statistics, other.Statistics) && CacheId.Equals(other);
        }

        /// <nodoc />
        public static bool operator ==(CacheSessionStatistics left, CacheSessionStatistics right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(CacheSessionStatistics left, CacheSessionStatistics right)
        {
            return !left.Equals(right);
        }
    }
}
