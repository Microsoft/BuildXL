// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

namespace BuildXL.Cache.ContentStore.Interfaces.Sessions
{
    /// <summary>
    ///     Extended features for sessions that can hibernate/restore.
    /// </summary>
    public interface IHibernateContentSession
    {
        /// <summary>
        ///     Retrieve collection of content hashes currently pinned in the session.
        /// </summary>
        /// <returns>
        ///     Collection of content hashes for content that is currently pinned.
        /// </returns>
        IEnumerable<ContentHash> EnumeratePinnedContentHashes();

        /// <summary>
        ///     Restore pinning of a collection of content hashes in the current session.
        /// </summary>
        /// <param name="context">
        ///     Tracing context.
        /// </param>
        /// <param name="contentHashes">
        ///     Collection of content hashes to be pinned.
        /// </param>
        Task PinBulkAsync(Context context, IEnumerable<ContentHash> contentHashes);

        /// <summary>
        ///     Shuts down quota keeper to prevent further eviction of content
        /// </summary>
        Task<BoolResult> ShutdownEvictionAsync(Context context);
    }
}
