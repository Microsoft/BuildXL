// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Stores;

namespace BuildXL.Cache.MemoizationStore.Distributed.Stores
{
    /// <summary>
    /// Defines a database which stores memoization information
    /// </summary>
    public class DistributedMemoizationDatabase : MemoizationDatabase
    {
        private readonly MemoizationDatabase _localDatabase;
        private readonly MemoizationDatabase _sharedDatabase;
        private readonly LocalLocationStore _localLocationStore;

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(DistributedMemoizationDatabase));

        /// <nodoc />
        public DistributedMemoizationDatabase(LocalLocationStore localLocationStore, MemoizationDatabase sharedDatabase)
        {
            _localDatabase = new RocksDbMemoizationDatabase(localLocationStore.Database, ownsDatabase: false);
            _sharedDatabase = sharedDatabase;
            _localLocationStore = localLocationStore;
        }

        /// <inheritdoc />
        protected override async Task<Result<bool>> CompareExchangeCore(
            OperationContext context, 
            StrongFingerprint strongFingerprint, 
            string replacementToken, 
            ContentHashListWithDeterminism expected, 
            ContentHashListWithDeterminism replacement)
        {
            var result = await _sharedDatabase.CompareExchange(context, strongFingerprint, replacementToken, expected, replacement);
            if (!result.Succeeded || !result.Value)
            {
                return result;
            }

            // Successfully updated the entry. Notify the event store.
            await _localLocationStore.EventStore.UpdateMetadataEntryAsync(context, 
                new UpdateMetadataEntryEventData(
                    _localLocationStore.ClusterState.PrimaryMachineId, 
                    strongFingerprint, 
                    new MetadataEntry(replacement, _localLocationStore.EventStore.Clock.UtcNow.ToFileTimeUtc()))).ThrowIfFailure();
            return result;
        }

        /// <inheritdoc />
        public override Task<IEnumerable<StructResult<StrongFingerprint>>> EnumerateStrongFingerprintsAsync(OperationContext context)
        {
            return _localDatabase.EnumerateStrongFingerprintsAsync(context);
        }

        /// <inheritdoc />
        protected override async Task<Result<(ContentHashListWithDeterminism contentHashListInfo, string replacementToken)>> GetContentHashListCoreAsync(OperationContext context, StrongFingerprint strongFingerprint, bool preferShared)
        {
            var firstDatabase = preferShared ? _sharedDatabase : _localDatabase;
            var secondDatabase = preferShared ? _localDatabase : _sharedDatabase;

            var firstResult = await firstDatabase.GetContentHashListAsync(context, strongFingerprint, preferShared);
            if (!firstResult.Succeeded || firstResult.Value.contentHashListInfo.ContentHashList != null)
            {
                return firstResult;
            }

            return await secondDatabase.GetContentHashListAsync(context, strongFingerprint, preferShared);
        }

        /// <inheritdoc />
        protected override async Task<Result<LevelSelectors>> GetLevelSelectorsCoreAsync(OperationContext context, Fingerprint weakFingerprint, int level)
        {
            if (level == 0)
            {
                var result = await _localDatabase.GetLevelSelectorsAsync(context, weakFingerprint, level);
                return result.Select(l => new LevelSelectors(l.Selectors, hasMore: true));
            }
            else
            {
                return await _sharedDatabase.GetLevelSelectorsAsync(context, weakFingerprint, level - 1);
            }
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            await _sharedDatabase.StartupAsync(context).ThrowIfFailure();
            await _localDatabase.StartupAsync(context).ThrowIfFailure();
            return BoolResult.Success;
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            var success = BoolResult.Success;
            success &= await _localDatabase.ShutdownAsync(context);
            success &= await _sharedDatabase.ShutdownAsync(context);
            return success;
        }
    }
}
