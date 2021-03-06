// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    ///     Interface for repair handling.
    /// </summary>
    public delegate Task<Result<long>> TrimBulkAsync(Context context, IEnumerable<ContentHash> contentHashes, CancellationToken cts, UrgencyHint urgencyHint);

    /// <summary>
    ///     Extended features for content stores to support repair handling.
    /// </summary>
    public interface IRepairStore
    {
        /// <summary>
        ///     Removes local content location for a set of content hashes. Returns number of hashes removed.
        /// </summary>
        Task<Result<long>> RemoveFromTrackerAsync(Context context);
    }
}
