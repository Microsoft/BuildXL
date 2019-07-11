// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Tracing;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.KeyValueStores;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Threading;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// RocksDb-based version of <see cref="ContentLocationDatabase"/>.
    /// </summary>
    public sealed class RocksDbContentLocationDatabase : ContentLocationDatabase
    {
        private readonly RocksDbContentLocationDatabaseConfiguration _configuration;

        private KeyValueStoreGuard _keyValueStore;
        private const string ActiveStoreSlotFileName = "activeSlot.txt";
        private StoreSlot _activeSlot = StoreSlot.Slot1;
        private string _storeLocation;
        private readonly string _activeSlotFilePath;        

        private static readonly byte[] EmptyBytes = CollectionUtilities.EmptyArray<byte>();

        private enum StoreSlot
        {
            Slot1,
            Slot2
        }

        /// <summary>
        /// There's multiple column families in this usage of RocksDB.
        ///
        /// The default column family is used to store a <see cref="ContentHash"/> to <see cref="ContentLocationEntry"/> mapping, which has been
        /// the usage since this started.
        ///
        /// All others are documented below.
        /// </summary>
        private enum Columns
        {
            ClusterState,
            /// <summary>
            /// Stores mapping from <see cref="StrongFingerprint"/> to a <see cref="ContentHashList"/>. This allows us
            /// to look up via a <see cref="Fingerprint"/>, or a <see cref="StrongFingerprint"/>. The only reason we
            /// can look up by <see cref="Fingerprint"/> is that it is stored as a prefix to the
            /// <see cref="StrongFingerprint"/>.
            ///
            /// This serves all of CaChaaS' needs for storage, modulo garbage collection.
            /// </summary>
            Metadata
        }

        private enum ClusterStateKeys
        {
            MaxMachineId
        }

        /// <inheritdoc />
        public RocksDbContentLocationDatabase(IClock clock, RocksDbContentLocationDatabaseConfiguration configuration, Func<IReadOnlyList<MachineId>> getInactiveMachines)
            : base(clock, configuration, getInactiveMachines)
        {
            _configuration = configuration;
            _activeSlotFilePath = (_configuration.StoreLocation / ActiveStoreSlotFileName).ToString();
        }

        /// <inheritdoc />
        protected override Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            _keyValueStore?.Dispose();
            return base.ShutdownCoreAsync(context);
        }

        /// <inheritdoc />
        protected override BoolResult InitializeCore(OperationContext context)
        {
            var result = Load(context, GetActiveSlot(context.TracingContext), clean: _configuration.CleanOnInitialize);
            if (result && _configuration.TestInitialCheckpointPath != null)
            {
                return RestoreCheckpoint(context, _configuration.TestInitialCheckpointPath);
            }

            return result;
        }

        private BoolResult Load(OperationContext context, StoreSlot activeSlot, bool clean = false)
        {
            try
            {
                var storeLocation = GetStoreLocation(activeSlot);

                if (clean && Directory.Exists(storeLocation))
                {
                    BuildXL.Native.IO.FileUtilities.DeleteDirectoryContents(storeLocation, deleteRootDirectory: true);
                }

                Directory.CreateDirectory(storeLocation);

                Tracer.Info(context, $"Creating rocksdb store at '{storeLocation}'.");

                var possibleStore = KeyValueStoreAccessor.Open(storeLocation,
                    additionalColumns: new[] { nameof(ClusterState), "Metadata" });
                if (possibleStore.Succeeded)
                {
                    var oldKeyValueStore = _keyValueStore;
                    var store = possibleStore.Result;

                    if (oldKeyValueStore == null)
                    {
                        _keyValueStore = new KeyValueStoreGuard(store);
                    }
                    else
                    {
                        // Just replace the inner accessor
                        oldKeyValueStore.Replace(store);
                    }

                    _activeSlot = activeSlot;
                    _storeLocation = storeLocation;
                }

                return possibleStore.Succeeded ? BoolResult.Success : new BoolResult($"Failed to initialize a RocksDb store at {_storeLocation}:", possibleStore.Failure.DescribeIncludingInnerFailures());
            }
            catch (Exception ex) when (ex.IsRecoverableIoException())
            {
                return new BoolResult(ex);
            }
        }

        private StoreSlot GetNextSlot(StoreSlot slot)
        {
            return slot == StoreSlot.Slot1 ? StoreSlot.Slot2 : StoreSlot.Slot1;
        }

        private void SaveActiveSlot(Context context)
        {
            try
            {
                File.WriteAllText(_activeSlotFilePath, _activeSlot.ToString());
            }
            catch (Exception ex) when (ex.IsRecoverableIoException())
            {
                Tracer.Warning(context, $"Failure getting active slot from {_activeSlotFilePath}: {ex}");
            }
        }

        private StoreSlot GetActiveSlot(Context context)
        {
            try
            {
                if (File.Exists(_activeSlotFilePath))
                {
                    var activeSlotString = File.ReadAllText(_activeSlotFilePath);
                    if (Enum.TryParse(activeSlotString, out StoreSlot slot))
                    {
                        return slot;
                    }
                }
            }
            catch (Exception ex) when (ex.IsRecoverableIoException())
            {
                Tracer.Warning(context, $"Failure getting active slot from {_activeSlotFilePath}: {ex}");
            }

            return StoreSlot.Slot1;
        }

        private string GetStoreLocation(StoreSlot slot)
        {
            return (_configuration.StoreLocation / slot.ToString()).ToString();
        }

        /// <inheritdoc />
        protected override BoolResult SaveCheckpointCore(OperationContext context, AbsolutePath checkpointDirectory)
        {
            try
            {
                var targetDirectory = checkpointDirectory.ToString();
                Tracer.Info(context.TracingContext, $"Saving content location database checkpoint to '{targetDirectory}'.");

                if (Directory.Exists(targetDirectory))
                {
                    FileUtilities.DeleteDirectoryContents(targetDirectory, deleteRootDirectory: true);
                }

                return _keyValueStore.Use(store => store.SaveCheckpoint(targetDirectory)).ToBoolResult();
            }
            catch (Exception ex) when (ex.IsRecoverableIoException())
            {
                return new BoolResult(ex, "Save checkpoint failed.");
            }
        }

        /// <inheritdoc />
        protected override BoolResult RestoreCheckpointCore(OperationContext context, AbsolutePath checkpointDirectory)
        {
            try
            {
                var nextActiveSlot = GetNextSlot(_activeSlot);
                var newStoreLocation = GetStoreLocation(nextActiveSlot);

                Tracer.Info(context.TracingContext, $"Loading content location database checkpoint from '{checkpointDirectory}' into '{newStoreLocation}'.");

                if (Directory.Exists(newStoreLocation))
                {
                    FileUtilities.DeleteDirectoryContents(newStoreLocation, deleteRootDirectory: true);
                }

                Directory.Move(checkpointDirectory.ToString(), newStoreLocation);

                var possiblyLoaded = Load(context, nextActiveSlot);
                if (possiblyLoaded.Succeeded)
                {
                    SaveActiveSlot(context.TracingContext);
                }

                return possiblyLoaded;
            }
            catch (Exception ex) when (ex.IsRecoverableIoException())
            {
                return new BoolResult(ex, "Restore checkpoint failed.");
            }
        }

        /// <inheritdoc />
        public override bool IsImmutable(AbsolutePath dbFile)
        {
            return dbFile.Path.EndsWith(".sst", StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc />
        protected override void UpdateClusterStateCore(OperationContext context, ClusterState clusterState, bool write)
        {
            _keyValueStore.Use(
                    store =>
                    {
                        int maxMachineId = ClusterState.InvalidMachineId;
                        if (!store.TryGetValue(nameof(ClusterStateKeys.MaxMachineId), out var maxMachinesString, nameof(Columns.ClusterState)) ||
                            !int.TryParse(maxMachinesString, out maxMachineId))
                        {
                            Tracer.OperationDebug(context, $"Unable to load cluster state from db. MaxMachineId='{maxMachinesString}'");
                            if (!write)
                            {
                                // No machine state in db. Return if we are not updating the db.
                                return;
                            }
                        }

                        void logSynchronize()
                        {
                            Tracer.OperationDebug(context, $"Synchronizing cluster state: MaxMachineId={clusterState.MaxMachineId}, Database.MaxMachineId={maxMachineId}]");
                        }

                        if (clusterState.MaxMachineId > maxMachineId && write)
                        {
                            logSynchronize();

                            // Update db with values from cluster state
                            for (int machineIndex = maxMachineId + 1; machineIndex <= clusterState.MaxMachineId; machineIndex++)
                            {
                                if (clusterState.TryResolve(new MachineId(machineIndex), out var machineLocation))
                                {
                                    Tracer.OperationDebug(context, $"Storing machine mapping ({machineIndex}={machineLocation})");
                                    store.Put(machineIndex.ToString(), machineLocation.Path, nameof(Columns.ClusterState));
                                }
                                else
                                {
                                    throw Contract.AssertFailure($"Unabled to resolve machine location for machine id={machineIndex}");
                                }
                            }

                            store.Put(nameof(ClusterStateKeys.MaxMachineId), clusterState.MaxMachineId.ToString(), nameof(Columns.ClusterState));
                        }
                        else if (maxMachineId > clusterState.MaxMachineId)
                        {
                            logSynchronize();

                            // Update cluster state with values from db
                            var unknownMachines = new Dictionary<MachineId, MachineLocation>();
                            for (int machineIndex = clusterState.MaxMachineId + 1; machineIndex <= maxMachineId; machineIndex++)
                            {
                                if (store.TryGetValue(machineIndex.ToString(), out var machineLocationData, nameof(Columns.ClusterState)))
                                {
                                    var machineId = new MachineId(machineIndex);
                                    var machineLocation = new MachineLocation(machineLocationData);
                                    context.LogMachineMapping(Tracer, machineId, machineLocation);
                                    unknownMachines[machineId] = machineLocation;
                                }
                                else
                                {
                                    throw Contract.AssertFailure($"Unabled to find machine location for machine id={machineIndex}");
                                }
                            }

                            clusterState.AddUnknownMachines(maxMachineId, unknownMachines);
                        }
                    }).ThrowOnError();
        }

        /// <inheritdoc />
        protected override IEnumerable<ShortHash> EnumerateSortedKeysFromStorage(CancellationToken token)
        {
            var keyBuffer = new List<ShortHash>();
            byte[] startValue = null;

            const int KeysChunkSize = 100000;
            while (!token.IsCancellationRequested)
            {
                keyBuffer.Clear();

                _keyValueStore.Use(
                    store =>
                    {
                        // NOTE: Use the garbage collect procedure to collect which keys to garbage collect. This is
                        // different than the typical use which actually collects the keys specified by the garbage collector.
                        var cts = new CancellationTokenSource();
                        using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, token))
                        {
                            store.GarbageCollect(
                                canCollect: key =>
                                {
                                    if (keyBuffer.Count == 0 && ByteArrayComparer.Instance.Equals(startValue, key))
                                    {
                                        // Start value is the same as the key. Skip it to keep from double processing the start value.
                                        return false;
                                    }

                                    keyBuffer.Add(DeserializeKey(key));
                                    startValue = key;

                                    if (keyBuffer.Count == KeysChunkSize)
                                    {
                                        cts.Cancel();
                                    }

                                    return false;
                                },
                                cancellationToken: cancellation.Token,
                                startValue: startValue);
                        }

                    }).ThrowOnError();

                if (keyBuffer.Count == 0)
                {
                    break;
                }

                foreach (var key in keyBuffer)
                {
                    yield return key;
                }
            }
        }

        /// <inheritdoc />
        protected override IEnumerable<(ShortHash key, ContentLocationEntry entry)> EnumerateEntriesWithSortedKeysFromStorage(
            CancellationToken token,
            EnumerationFilter filter = null)
        {
            var keyBuffer = new List<(ShortHash key, ContentLocationEntry entry)>();
            const int KeysChunkSize = 100000;
            byte[] startValue = null;
            while (!token.IsCancellationRequested)
            {
                keyBuffer.Clear();

                _keyValueStore.Use(
                    store =>
                    {
                        // NOTE: Use the garbage collect procedure to collect which keys to garbage collect. This is
                        // different than the typical use which actually collects the keys specified by the garbage collector.
                        var cts = new CancellationTokenSource();
                        using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, token))
                        {
                            store.GarbageCollectByKeyValue(
                                canCollect: (iterator) =>
                                {
                                    byte[] key = null;
                                    if (keyBuffer.Count == 0 && ByteArrayComparer.Instance.Equals(startValue, key = iterator.Key()))
                                    {
                                        // Start value is the same as the key. Skip it to keep from double processing the start value.

                                        // Set startValue to null to indicate that we potentially could have reached the end of the database.
                                        startValue = null;
                                        return false;
                                    }

                                    startValue = null;
                                    byte[] value = null;
                                    if (filter != null && filter(value = iterator.Value()))
                                    {
                                        keyBuffer.Add((DeserializeKey(key ?? iterator.Key()), DeserializeContentLocationEntry(value)));
                                    }

                                    if (keyBuffer.Count == KeysChunkSize)
                                    {
                                        // We reached the limit for a current chunk.
                                        // Reading the iterator to get the new start value.
                                        startValue = iterator.Key();
                                        cts.Cancel();
                                    }

                                    return false;
                                },
                                cancellationToken: cancellation.Token,
                                startValue: startValue);
                        }

                    }).ThrowOnError();

                foreach (var key in keyBuffer)
                {
                    yield return key;
                }

                // Null value in startValue variable means that the database reached it's end.
                if (startValue == null)
                {
                    break;
                }
            }
        }

        /// <inheritdoc />
        protected override bool TryGetEntryCoreFromStorage(OperationContext context, ShortHash hash, out ContentLocationEntry entry)
        {
            entry = _keyValueStore.Use(
                    (store, state) => TryGetEntryCoreHelper(state.hash, store, state.db),
                    (hash, db: this)
                ).ThrowOnError();
            return entry != null;
        }

        // NOTE: This should remain static to avoid allocations in TryGetEntryCore
        private static ContentLocationEntry TryGetEntryCoreHelper(ShortHash hash, IBuildXLKeyValueStore store, RocksDbContentLocationDatabase db)
        {
            ContentLocationEntry result = null;
            if (store.TryGetValue(db.GetKey(hash), out var data))
            {
                result = db.DeserializeContentLocationEntry(data);
            }

            return result;
        }

        /// <inheritdoc />
        internal override void Persist(OperationContext context, ShortHash hash, ContentLocationEntry entry)
        {
            if (entry == null)
            {
                DeleteFromDb(context, hash);
            }
            else
            {
                SaveToDb(context, hash, entry);
            }
        }

        /// <inheritdoc />
        internal override void PersistBatch(OperationContext context, IEnumerable<KeyValuePair<ShortHash, ContentLocationEntry>> pairs)
        {
            _keyValueStore.Use((store, state) => PersistBatchHelper(store, state.pairs, state.db), (pairs, db: this)).ThrowOnError();
        }

        private static Unit PersistBatchHelper(IBuildXLKeyValueStore store, IEnumerable<KeyValuePair<ShortHash, ContentLocationEntry>> pairs, RocksDbContentLocationDatabase db)
        {
            store.ApplyBatch(
                pairs.Select(pair => db.GetKey(pair.Key)),
                pairs.Select(pair => pair.Value != null ? db.SerializeContentLocationEntry(pair.Value) : null));
            return Unit.Void;
        }

        private void SaveToDb(OperationContext context, ShortHash hash, ContentLocationEntry entry)
        {
            _keyValueStore.Use(
                (store, state) => SaveToDbHelper(state.hash, state.entry, store, state.db), (hash, entry, db: this)).ThrowOnError();
        }

        // NOTE: This should remain static to avoid allocations in Store
        private static Unit SaveToDbHelper(ShortHash hash, ContentLocationEntry entry, IBuildXLKeyValueStore store, RocksDbContentLocationDatabase db)
        {
            var value = db.SerializeContentLocationEntry(entry);
            store.Put(db.GetKey(hash), value);

            return Unit.Void;
        }

        private void DeleteFromDb(OperationContext context, ShortHash hash)
        {
            _keyValueStore.Use(
                (store, state) => DeleteFromDbHelper(state.hash, store, state.db), (hash, db: this)).ThrowOnError();
        }

        // NOTE: This should remain static to avoid allocations in Delete
        private static Unit DeleteFromDbHelper(ShortHash hash, IBuildXLKeyValueStore store, RocksDbContentLocationDatabase db)
        {
            store.Remove(db.GetKey(hash));
            return Unit.Void;
        }

        private ShortHash DeserializeKey(byte[] key)
        {
            return new ShortHash(new FixedBytes(key));
        }

        private byte[] GetKey(ShortHash hash)
        {
            return hash.ToByteArray();
        }

        // TODO(jubayard): garbage collection / removal in general

        /// <inheritdoc />
        public override GetContentHashListResult GetContentHashList(OperationContext context, StrongFingerprint strongFingerprint)
        {
            return context.PerformOperation(
                Tracer,
                () =>
                {
                    var key = GetMetadataKey(strongFingerprint);
                    ContentHashListWithDeterminism? result = null;
                    var status = _keyValueStore.Use(
                        store =>
                        {
                            if (store.TryGetValue(key, out var data, nameof(Columns.Metadata)))
                            {
                                result = DeserializeContentHashList(data);
                                // TODO(jubayard): since we are inside the ContentLocationDatabase, we can validate that all
                                // hashes exist. Moreover, we can prune content.
                            }
                        });

                    if (!status.Succeeded)
                    {
                        return new GetContentHashListResult(status.Failure.CreateException());
                    }

                    if (result == null)
                    {
                        return new GetContentHashListResult(CacheMiss(CacheDeterminism.None));
                    }

                    return new GetContentHashListResult(result.Value);
                }, Counters[ContentLocationDatabaseCounters.GetContentHashList]);
        }
        
        /// <inheritdoc />
        public override AddOrGetContentHashListResult AddOrGetContentHashList(OperationContext context, StrongFingerprint strongFingerprint, ContentHashListWithDeterminism contentHashListWithDeterminism)
        {
            return context.PerformOperation(
                Tracer,
                () =>
                {
                    var key = GetMetadataKey(strongFingerprint);

                    var status = _keyValueStore.Use(
                        store =>
                        {
                            // TODO(jubayard): RocksDB supports transactional semantics that would make this logic better
                            var contentHashList = contentHashListWithDeterminism.ContentHashList;
                            var determinism = contentHashListWithDeterminism.Determinism;

                            // Load old value
                            var oldContentHashListWithDeterminism = GetContentHashList(context, strongFingerprint);
                            var oldContentHashList = oldContentHashListWithDeterminism.ContentHashListWithDeterminism.ContentHashList;
                            var oldDeterminism = oldContentHashListWithDeterminism.ContentHashListWithDeterminism.Determinism;

                            // Make sure we're not mixing SinglePhaseNonDeterminism records
                            if (oldContentHashList != null &&
                                        oldDeterminism.IsSinglePhaseNonDeterministic != determinism.IsSinglePhaseNonDeterministic)
                            {
                                return AddOrGetContentHashListResult.SinglePhaseMixingError;
                            }

                            // Replace if incoming has better determinism or some content for the existing entry is missing.
                            if (oldContentHashList == null || oldDeterminism.ShouldBeReplacedWith(determinism) ||
                                        !IsContentAvailable(context, oldContentHashList))
                            {
                                // TODO(jubayard): SQLite impl runs this with exclusive access to the DB to avoid data races. We have no way to do this here.
                                store.Put(key, SerializeContentHashList(contentHashListWithDeterminism), nameof(Columns.Metadata));

                                // Accept the value
                                return new AddOrGetContentHashListResult(CacheMiss(determinism));
                            }

                            // If we didn't accept the new value because it is the same as before, just with a not
                            // necessarily better determinism, then let the user know.
                            if (oldContentHashList != null && oldContentHashList.Equals(contentHashList))
                            {
                                return new AddOrGetContentHashListResult(CacheMiss(oldDeterminism));
                            }

                            // If we didn't accept a deterministic tool's data, then we're in an inconsistent state
                            if (determinism.IsDeterministicTool)
                            {
                                return new AddOrGetContentHashListResult(
                                    AddOrGetContentHashListResult.ResultCode.InvalidToolDeterminismError,
                                    oldContentHashListWithDeterminism.ContentHashListWithDeterminism);
                            }

                            // If we did not accept the given value, return the value in the cache
                            return new AddOrGetContentHashListResult(oldContentHashListWithDeterminism);
                        });

                    // Success for this status here may actually be an error that's been returned
                    return status.Succeeded ? status.Result : new AddOrGetContentHashListResult(status.Failure.CreateException());
                }, Counters[ContentLocationDatabaseCounters.AddOrGetContentHashList]);
        }
        
        private ContentHashListWithDeterminism CacheMiss(CacheDeterminism determinism)
        {
            return new ContentHashListWithDeterminism(null, determinism);
        }

        /// <inheritdoc />
        public override IEnumerable<StructResult<StrongFingerprint>> EnumerateStrongFingerprints(OperationContext context)
        {
            var result = new List<StructResult<StrongFingerprint>>();
            var status = _keyValueStore.Use(
                store =>
                {
                    foreach (var kvp in store.PrefixSearch((byte[])null, nameof(Columns.Metadata)))
                    {
                        var strongFingerprint = DeserializeStrongFingerprint(kvp.Key);
                        result.Add(StructResult.Create(strongFingerprint));
                    }

                    return result;
                });

            if (!status.Succeeded)
            {
                result.Add(new StructResult<StrongFingerprint>(status.Failure.CreateException()));
            }

            return result;
        }

        private bool IsContentAvailable(OperationContext context, ContentHashList contentHashList)
        {
            // TODO(jubayard): implement. Since this needs to pin stuff, we need it to happen in a higher layer.
            return true;
        }

        /// <inheritdoc />
        public override IReadOnlyCollection<GetSelectorResult> GetSelectors(OperationContext context, Fingerprint weakFingerprint)
        {
            var result = new List<GetSelectorResult>();

            var status = _keyValueStore.Use(
                store =>
                {
                    var key = SerializeWeakFingerprint(weakFingerprint);

                    // This only works because the strong fingerprint serializes the weak fingerprint first. Hence,
                    // we know that all keys here are strong fingerprints that match the weak fingerprint.
                    foreach (var kvp in store.PrefixSearch(key, columnFamilyName: nameof(Columns.Metadata)))
                    {
                        var strongFingerprint = DeserializeStrongFingerprint(kvp.Key);
                        result.Add(new GetSelectorResult(strongFingerprint.Selector));
                    }
                });

            if (!status.Succeeded)
            {
                result.Add(new GetSelectorResult(status.Failure.CreateException()));
            }

            return result;
        }

        private byte[] SerializeWeakFingerprint(Fingerprint weakFingerprint)
        {
            return SerializeCore(weakFingerprint, (instance, writer) => instance.Serialize(writer));
        }

        private byte[] SerializeStrongFingerprint(StrongFingerprint strongFingerprint)
        {
            return SerializeCore(strongFingerprint, (instance, writer) => instance.Serialize(writer));
        }

        private StrongFingerprint DeserializeStrongFingerprint(byte[] bytes)
        {
            return DeserializeCore(bytes, reader => StrongFingerprint.Deserialize(reader));
        }

        private byte[] SerializeContentHashList(ContentHashListWithDeterminism value)
        {
            return SerializeCore(value, (instance, writer) => instance.Serialize(writer));
        }

        private ContentHashListWithDeterminism DeserializeContentHashList(byte[] data)
        {
            return DeserializeCore(data, reader => ContentHashListWithDeterminism.Deserialize(reader));
        }

        private byte[] GetMetadataKey(StrongFingerprint strongFingerprint)
        {
            return SerializeStrongFingerprint(strongFingerprint);
        }

        private class KeyValueStoreGuard : IDisposable
        {
            private KeyValueStoreAccessor _accessor;
            private readonly ReadWriteLock _rwLock = ReadWriteLock.Create();

            public KeyValueStoreGuard(KeyValueStoreAccessor accessor)
            {
                _accessor = accessor;
            }

            public void Dispose()
            {
                using (_rwLock.AcquireWriteLock())
                {
                    _accessor.Dispose();
                }
            }

            public void Replace(KeyValueStoreAccessor accessor)
            {
                using (_rwLock.AcquireWriteLock())
                {
                    _accessor.Dispose();
                    _accessor = accessor;
                }
            }

            public Possible<TResult> Use<TState, TResult>(Func<IBuildXLKeyValueStore, TState, TResult> action, TState state)
            {
                using (_rwLock.AcquireReadLock())
                {
                    return _accessor.Use(action, state);
                }
            }

            public Possible<Unit> Use(Action<IBuildXLKeyValueStore> action)
            {
                using (_rwLock.AcquireReadLock())
                {
                    return _accessor.Use(action);
                }
            }

            public Possible<TResult> Use<TResult>(Func<IBuildXLKeyValueStore, TResult> action)
            {
                using (_rwLock.AcquireReadLock())
                {
                    return _accessor.Use(action);
                }
            }
        }
    }
}
