// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;

// ReSharper disable All
namespace BuildXL.Cache.ContentStore.Distributed
{
    /// <summary>
    /// A store that provides content locations for peer to peer content sessions.
    /// </summary>
    /// <remarks>
    /// An interface representing a store that persists content locations
    /// and can retrieve content locations (locations between cache machines)
    /// that provide data about where to find content in a P2P file transfer system.
    /// </remarks>
    public interface IContentLocationStore : IStartupShutdown
    {
        /// <summary>
        /// Returns machine reputation tracker.
        /// </summary>
        MachineReputationTracker MachineReputationTracker { get; }

        /// <summary>
        /// Enumerates the content locations for a given set of content hashes.
        /// </summary>
        Task<GetBulkLocationsResult> GetBulkAsync(Context context, IReadOnlyList<ContentHash> contentHashes, CancellationToken cts, UrgencyHint urgencyHint, GetBulkOrigin origin);

        /// <summary>
        /// Removes local content location (i.e. the current machine) for a set of content hashes.
        /// </summary>
        Task<BoolResult> TrimBulkAsync(Context context, IReadOnlyList<ContentHash> contentHashes, CancellationToken cts, UrgencyHint urgencyHint);

        /// <summary>
        /// Invalidates all content for the machine in the content location store
        /// </summary>
        Task<BoolResult> InvalidateLocalMachineAsync(Context context, CancellationToken cts);

        /// <summary>
        /// Updates the expiry of provided hashes in the content tracker to current time.
        /// </summary>
        Task<BoolResult> TouchBulkAsync(Context context, IReadOnlyList<ContentHashWithSize> contentHashes, CancellationToken cts, UrgencyHint urgencyHint);

        /// <summary>
        /// Gets the page size used to do bulk calls into the content location store.
        /// </summary>
        int PageSize { get; }

        /// <summary>
        /// Gets the counters.
        /// </summary>
        CounterSet GetCounters(Context context);

        /// <summary>
        /// Registers the current machine has the content for the given hash
        /// </summary>
        Task<BoolResult> RegisterLocalLocationAsync(Context context, IReadOnlyList<ContentHashWithSize> contentHashes, CancellationToken cts, UrgencyHint urgencyHint, bool touch = true);

        /// <summary>
        /// Puts a blob into the content location store.
        /// </summary>
        Task<BoolResult> PutBlobAsync(OperationContext context, ContentHash contentHash, byte[] blob);

        /// <summary>
        /// Gets a blob from the content location store. Fails if the blob is not found.
        /// </summary>
        Task<GetBlobResult> GetBlobAsync(OperationContext context, ContentHash contentHash);

        /// <summary>
        /// Gets whether the content location store supports blobs.
        /// </summary>
        bool AreBlobsSupported { get; }

        /// <summary>
        /// Gets the max size for blobs.
        /// </summary>
        long MaxBlobSize { get; }

        /// <summary>
        /// Reports about new reputation for a given location.
        /// </summary>
        void ReportReputation(MachineLocation location, MachineReputation reputation);

        /// <summary>
        /// Returns a random machine location, excluding the specified location. Returns default if operation is not possible.
        /// </summary>
        Result<MachineLocation> GetRandomMachineLocation(IReadOnlyList<MachineLocation> except);

        /// <summary>
        /// Returns the designated locations for a particular hash.
        /// </summary>
        Result<MachineLocation[]> GetDesignatedLocations(ContentHash hash);

        /// <summary>
        /// Determines whether a machine is active or not.
        /// </summary>
        bool IsMachineActive(MachineLocation m);
    }

    /// <summary>
    /// Set of extension methods for <see cref="IContentLocationStore"/> interface.
    /// </summary>
    public static class ContentLocationStoreExtensions
    {
        /// <summary>
        /// Retrieves the content locations for a given set of content hashes from local and global stores.
        /// </summary>
        public static IEnumerable<Task<GetBulkLocationsResult>> MultiLevelGetLocations(
            this IContentLocationStore store,
            Context context,
            IReadOnlyList<ContentHash> contentHashes,
            CancellationToken cts,
            UrgencyHint urgencyHint,
            bool subtractLocalResults)
        {
            var localResultsTask = store.GetBulkAsync(context, contentHashes, cts, urgencyHint, GetBulkOrigin.Local);
            yield return localResultsTask;

            yield return getBulkGlobal();

            // Local function: Get global results optionally subtracting local results
            async Task<GetBulkLocationsResult> getBulkGlobal()
            {
                var globalResults = await store.GetBulkAsync(context, contentHashes, cts, urgencyHint, GetBulkOrigin.Global);
                if (subtractLocalResults)
                {
                    globalResults = globalResults.Subtract(await localResultsTask);
                }

                return globalResults;
            }
        }

        /// <summary>
        /// Retrieves the content locations for a given set of content hashes where Global origin returns merged global and local content locations.
        /// </summary>
        public static async Task<GetBulkLocationsResult> GetBulkStackedAsync(
            this IContentLocationStore store,
            Context context,
            IReadOnlyList<ContentHash> contentHashes,
            CancellationToken cts,
            UrgencyHint urgencyHint,
            GetBulkOrigin origin)
        {
            var localResults = await store.GetBulkAsync(context, contentHashes, cts, urgencyHint, GetBulkOrigin.Local);
            if (origin == GetBulkOrigin.Local)
            {
                return localResults;
            }

            var globalResults = await store.GetBulkAsync(context, contentHashes, cts, urgencyHint, GetBulkOrigin.Global);

            return localResults.Merge(globalResults);
        }

        /// <summary>
        /// Retrieves the content locations for a given set of content hashes.
        /// </summary>
        public static Task<GetBulkLocationsResult> GetBulkAsync(
            this IContentLocationStore store,
            Context context,
            IReadOnlyList<ContentHash> contentHashes,
            CancellationToken cts,
            UrgencyHint urgencyHint)
        {
            return store.GetBulkAsync(context, contentHashes, cts, urgencyHint, GetBulkOrigin.Global);
        }        
    }
}
