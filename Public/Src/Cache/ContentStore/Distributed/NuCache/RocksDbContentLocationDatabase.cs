// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Tracing;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Synchronization;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.KeyValueStores;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tasks;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

#nullable enable annotations

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
        private Timer _compactionTimer;

        private readonly RocksDbLogsManager _logManager;

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
            /// What we effectively store is not a <see cref="ContentHashList"/>, but a <see cref="MetadataEntry"/>,
            /// which contains all information relevant to the database.
            ///
            /// This serves all of CaChaaS' needs for storage, modulo garbage collection.
            /// </summary>
            Metadata,
            DatabaseManagement,
        }

        private enum ClusterStateKeys
        {
            MaxMachineId,
            StoredEpoch
        }

        /// <inheritdoc />
        public RocksDbContentLocationDatabase(IClock clock, RocksDbContentLocationDatabaseConfiguration configuration, Func<IReadOnlyList<MachineId>> getInactiveMachines)
            : base(clock, configuration, getInactiveMachines)
        {
            Contract.Requires(configuration.MetadataGarbageCollectionMaximumNumberOfEntriesToKeep > 0);

            _configuration = configuration;
            _activeSlotFilePath = (_configuration.StoreLocation / ActiveStoreSlotFileName).ToString();

            if (_configuration.LogsBackupPath != null)
            {
                _logManager = new RocksDbLogsManager(clock, new PassThroughFileSystem(), _configuration.LogsBackupPath, _configuration.LogsRetention);
            }
        }

        /// <inheritdoc />
        protected override Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            lock (TimerChangeLock)
            {
                _compactionTimer?.Dispose();
                _compactionTimer = null;
            }

            _keyValueStore?.Dispose();

            return base.ShutdownCoreAsync(context);
        }

        /// <inheritdoc />
        protected override BoolResult InitializeCore(OperationContext context)
        {
            var result = InitialLoad(context, GetActiveSlot(context.TracingContext));
            if (result)
            {
                if (_configuration.TestInitialCheckpointPath != null)
                {
                    return RestoreCheckpoint(context, _configuration.TestInitialCheckpointPath);
                }

                if (_configuration.FullRangeCompactionInterval != Timeout.InfiniteTimeSpan)
                {
                    _compactionTimer = new Timer(
                        _ => FullRangeCompaction(context.CreateNested(nameof(RocksDbContentLocationDatabase), caller: nameof(FullRangeCompaction))),
                        null,
                        IsDatabaseWriteable ? _configuration.FullRangeCompactionInterval : Timeout.InfiniteTimeSpan,
                        Timeout.InfiniteTimeSpan);
                }
            }

            return result;
        }

        /// <inheritdoc />
        public override void SetDatabaseMode(bool isDatabaseWriteable)
        {
            if (IsDatabaseWriteable != isDatabaseWriteable)
            {
                // Shutdown can't happen simultaneously, so no need to take the lock
                var nextCompactionTimeSpan = isDatabaseWriteable ? _configuration.FullRangeCompactionInterval : Timeout.InfiniteTimeSpan;
                _compactionTimer?.Change(nextCompactionTimeSpan, Timeout.InfiniteTimeSpan);
            }

            base.SetDatabaseMode(isDatabaseWriteable);
        }

        private BoolResult InitialLoad(OperationContext context, StoreSlot activeSlot)
        {
            var clean = _configuration.CleanOnInitialize;

            // We backup the logs right before loading the first DB we load
            var storeLocation = GetStoreLocation(activeSlot);
            BackupLogs(context, storeLocation, name: $"InitialLoad{activeSlot}");

            var result = Load(context, activeSlot, clean);

            bool reload = false;

            if (!clean)
            {
                if (result.Succeeded)
                {
                    if (IsStoredEpochInvalid(out var epoch))
                    {
                        Counters[ContentLocationDatabaseCounters.EpochMismatches].Increment();
                        context.TraceDebug($"Stored epoch '{epoch}' does not match configured epoch '{_configuration.Epoch}'. Retrying with clean=true.");
                        reload = true;
                    }
                    else
                    {
                        Counters[ContentLocationDatabaseCounters.EpochMatches].Increment();
                    }
                }

                if (!result.Succeeded)
                {
                    context.TracingContext.Warning($"Failed to load database without cleaning. Retrying with clean=true. Failure: {result}");
                    reload = true;
                }
            }

            if (reload)
            {
                // If failed when cleaning is disabled, try again with forcing a clean
                return Load(context, GetNextSlot(activeSlot), clean: true);
            }

            return result;
        }

        private bool IsStoredEpochInvalid(out string epoch)
        {
            TryGetGlobalEntry(nameof(ClusterStateKeys.StoredEpoch), out epoch);
            return _configuration.Epoch != epoch;
        }

        private BoolResult Load(OperationContext context, StoreSlot activeSlot, bool clean)
        {
            try
            {
                var storeLocation = GetStoreLocation(activeSlot);

                if (clean)
                {
                    Counters[ContentLocationDatabaseCounters.DatabaseCleans].Increment();

                    if (Directory.Exists(storeLocation))
                    {
                        FileUtilities.DeleteDirectoryContents(storeLocation, deleteRootDirectory: true);
                    }
                }

                Directory.CreateDirectory(storeLocation);

                Tracer.Info(context, $"Creating RocksDb store at '{storeLocation}'. Clean={clean}, Configured Epoch='{_configuration.Epoch}'");

                var possibleStore = KeyValueStoreAccessor.Open(
                    new KeyValueStoreAccessor.RocksDbStoreArguments()
                    {
                        StoreDirectory = storeLocation,
                        AdditionalColumns = new[] { nameof(Columns.ClusterState), nameof(Columns.Metadata), nameof(Columns.DatabaseManagement) },
                        RotateLogsMaxFileSizeBytes = _configuration.LogsKeepLongTerm ? 0ul : ((ulong)"1MB".ToSize()),
                        RotateLogsNumFiles = _configuration.LogsKeepLongTerm ? 60ul : 1,
                        RotateLogsMaxAge = TimeSpan.FromHours(_configuration.LogsKeepLongTerm ? 12 : 1),
                        EnableStatistics = true,
                        FastOpen = true,
                        // The RocksDb database here is read-only from the perspective of the default column family,
                        // but read/write from the perspective of the ClusterState (which is rewritten on every
                        // heartbeat). This means that the database may perform background compactions on the column
                        // families, possibly triggering a RocksDb corruption "block checksum mismatch" error.
                        // Since the writes to ClusterState are relatively few, we can make-do with disabling
                        // compaction here and pretending like we are using a read-only database.
                        DisableAutomaticCompactions = !IsDatabaseWriteable,
                    },
                    // When an exception is caught from within methods using the database, this handler is called to
                    // decide whether the exception should be rethrown in user code, and the database invalidated. Our
                    // policy is to only invalidate if it is an exception coming from RocksDb, but not from our code.
                    failureHandler: failureEvent =>
                    {
                        // By default, rethrow is true iff it is a user error. We invalidate only if it isn't
                        failureEvent.Invalidate = !failureEvent.Rethrow;
                    },
                    // The database may be invalidated for a number of reasons, all related to latent bugs in our code.
                    // For example, exceptions thrown from methods that are operating on the DB. If that happens, we
                    // call a user-defined handler. This is because the instance is invalid after this happens.
                    invalidationHandler: failure => OnDatabaseInvalidated(context, failure),
                    // It is possible we may fail to open an already existing database. This can happen (most commonly)
                    // due to corruption, among others. If this happens, then we want to recreate it from empty. This
                    // only helps for the memoization store.
                    onFailureDeleteExistingStoreAndRetry: _configuration.OnFailureDeleteExistingStoreAndRetry,
                    // If the previous flag is true, and it does happen that we invalidate the database, we want to log
                    // it explicitly.
                    onStoreReset: failure =>
                    {
                        Tracer.Error(context, $"RocksDb critical error caused store to reset: {failure.DescribeIncludingInnerFailures()}");
                    });

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

        private void BackupLogs(OperationContext context, string instancePath, string name)
        {
            if (_logManager != null)
            {
                _logManager.BackupAsync(context, new AbsolutePath(instancePath), name).Result.IgnoreFailure();
                Task.Run(() => _logManager.GarbageCollect(context)).FireAndForget(context, severityOnException: Severity.Error);
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
                if (IsStoredEpochInvalid(out var storedEpoch))
                {
                    SetGlobalEntry(nameof(ClusterStateKeys.StoredEpoch), _configuration.Epoch);
                    Tracer.Info(context.TracingContext, $"Updated stored epoch from '{storedEpoch}' to '{_configuration.Epoch}'.");
                }

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
                LogMemoryUsage(context);

                var activeSlot = _activeSlot;

                var newActiveSlot = GetNextSlot(activeSlot);
                var newStoreLocation = GetStoreLocation(newActiveSlot);

                Tracer.Info(context.TracingContext, $"Loading content location database checkpoint from '{checkpointDirectory}' into '{newStoreLocation}'.");

                if (Directory.Exists(newStoreLocation))
                {
                    FileUtilities.DeleteDirectoryContents(newStoreLocation, deleteRootDirectory: true);
                }

                Directory.Move(checkpointDirectory.ToString(), newStoreLocation);

                var possiblyLoaded = Load(context, newActiveSlot, clean: false);
                if (possiblyLoaded.Succeeded)
                {
                    SaveActiveSlot(context.TracingContext);
                }

                // At this point in time, we have unloaded the old database and loaded the new one. This means we're
                // free to backup the old one's logs.
                var oldStoreLocation = GetStoreLocation(activeSlot);
                BackupLogs(context, oldStoreLocation, name: $"Restore{activeSlot}");

                return possiblyLoaded;
            }
            catch (Exception ex) when (ex.IsRecoverableIoException())
            {
                return new BoolResult(ex, "Restore checkpoint failed.");
            }
        }

        private void LogMemoryUsage(OperationContext context)
        {
            if (_keyValueStore != null)
            {
                try
                {
                    var usage = _keyValueStore.Use(store =>
                    {
                        // From the docs:
                        // 
                        // There are a couple of components in RocksDB that contribute to memory usage:
                        // Block cache
                        // Indexes and bloom filters
                        // Memtables
                        // Blocks pinned by iterators
                        // 
                        // Since we never create a cache, we don't need to check those.

                        long? indexesAndBloomFilters = null;
                        long? memtables = null;
                        if (long.TryParse(store.GetProperty("rocksdb.estimate-table-readers-mem"), out var val))
                        {
                            indexesAndBloomFilters = val;
                        }
                        if (long.TryParse(store.GetProperty("rocksdb.cur-size-all-mem-tables"), out val))
                        {
                            memtables = val;
                        }

                        return (indexesAndBloomFilters, memtables);
                    });

                    if (usage.Succeeded)
                    {
                        var (indexesAndBloomFilters, memtables) = usage.Result;
                        Tracer.Debug(context, $"Loading next checkpoint. Current database memory usage: IndexesAndBloomFilters={indexesAndBloomFilters}bytes, Memtables={memtables}bytes");
                    }
                    else
                    {
                        Tracer.Debug(context, $"Failed to get database memory usage: {usage.Failure.DescribeIncludingInnerFailures()}");
                    }
                }
                catch (Exception e)
                {
                    Tracer.Debug(context, $"Failed to get database memory usage. Exception: {e}");
                }
            }
        }

        /// <inheritdoc />
        public override bool IsImmutable(AbsolutePath dbFile)
        {
            return dbFile.Path.EndsWith(".sst", StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc />
        public override void SetGlobalEntry(string key, string value)
        {
            _keyValueStore.Use(store =>
            {
                if (value == null)
                {
                    store.Remove(key, nameof(Columns.ClusterState));
                }
                else
                {
                    store.Put(key, value, nameof(Columns.ClusterState));
                }
            }).ThrowOnError();
        }

        /// <inheritdoc />
        public override bool TryGetGlobalEntry(string key, out string value)
        {
            value = _keyValueStore.Use(store =>
            {
                if (store.TryGetValue(key, out var value, nameof(Columns.ClusterState)))
                {
                    return value;
                }
                else
                {
                    return null;
                }
            }).ThrowOnError();
            return value != null;
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
        protected override IEnumerable<ShortHash> EnumerateSortedKeysFromStorage(OperationContext context)
        {
            return EnumerateEntriesWithSortedKeysFromStorage(context, valueFilter: null, returnKeysOnly: true)
                .Select(pair => pair.key);
        }

        /// <inheritdoc />
        protected override IEnumerable<(ShortHash key, ContentLocationEntry entry)> EnumerateEntriesWithSortedKeysFromStorage(
            OperationContext context,
            EnumerationFilter? valueFilter,
            bool returnKeysOnly)
        {
            var token = context.Token;
            var keyBuffer = new List<(ShortHash key, ContentLocationEntry entry)>();
            // Last successfully processed entry, or before-the-start pointer
            var startValue = valueFilter?.StartingPoint?.ToByteArray();

            var reachedEnd = false;
            while (!token.IsCancellationRequested && !reachedEnd)
            {
                var processedKeys = 0;
                keyBuffer.Clear();

                var killSwitchUsed = false;
                context.PerformOperation(Tracer, () =>
                {
                    // NOTE: the killswitch may cause the GC to early stop. After it has been triggered, the next Use()
                    // call will resume with a different database instance.
                    return _keyValueStore.Use(
                        (store, killSwitch) =>
                        {
                            // NOTE: Use the garbage collect procedure to collect which keys to garbage collect. This is
                            // different than the typical use which actually collects the keys specified by the garbage collector.
                            using (var cts = new CancellationTokenSource())
                            using (var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, token, killSwitch))
                            {
                                var gcResult = store.GarbageCollectByKeyValue(
                                    canCollect: (iterator) =>
                                    {
                                        var key = iterator.Key();
                                        if (processedKeys == 0 && ByteArrayComparer.Instance.Equals(startValue, key))
                                        {
                                            // Start value is the same as the key. Skip it to keep from double processing the start value.
                                            return false;
                                        }

                                        if (returnKeysOnly)
                                        {
                                            keyBuffer.Add((DeserializeKey(key), null));
                                        }
                                        else
                                        {
                                            byte[] value = null;
                                            if (valueFilter?.ShouldEnumerate?.Invoke(value = iterator.Value()) == true)
                                            {
                                                keyBuffer.Add((DeserializeKey(key), DeserializeContentLocationEntry(value)));
                                            }
                                        }

                                        // We can only update this after the key has been successfully processed.
                                        startValue = key;
                                        processedKeys++;

                                        if (processedKeys == _configuration.EnumerateEntriesWithSortedKeysFromStorageBufferSize)
                                        {
                                            // We reached the limit for the current chunk. Iteration will get cancelled here,
                                            // which will set reachedEnd to false.
                                            cts.Cancel();
                                        }

                                        return false;
                                    },
                                    cancellationToken: cancellation.Token,
                                    startValue: startValue);

                                reachedEnd = gcResult.ReachedEnd;
                            }

                            killSwitchUsed = killSwitch.IsCancellationRequested;
                        }).ToBoolResult();
                }, messageFactory: _ => $"KillSwitch=[{killSwitchUsed}] ReturnKeysOnly=[{returnKeysOnly}] Canceled=[{token.IsCancellationRequested}]").ThrowIfFailure();

                foreach (var key in keyBuffer)
                {
                    yield return key;
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
        internal override void Persist(OperationContext context, ShortHash hash, ContentLocationEntry? entry)
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
            store.ApplyBatch(pairs.Select(
                kvp => new KeyValuePair<byte[], byte[]>(db.GetKey(kvp.Key), kvp.Value != null ? db.SerializeContentLocationEntry(kvp.Value) : null)));
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

        /// <inheritdoc />
        public override GetContentHashListResult GetContentHashList(OperationContext context, StrongFingerprint strongFingerprint)
        {
            var key = GetMetadataKey(strongFingerprint);
            ContentHashListWithDeterminism? result = null;
            var status = _keyValueStore.Use(
                store =>
                {
                    if (store.TryGetValue(key, out var data, nameof(Columns.Metadata)))
                    {
                        var metadata = DeserializeMetadataEntry(data);
                        result = metadata.ContentHashListWithDeterminism;

                        // Update the time, only if no one else has changed it in the mean time. We don't
                        // really care if this succeeds or not, because if it doesn't it only means someone
                        // else changed the stored value before this operation but after it was read.
                        Analysis.IgnoreResult(this.CompareExchange(context, strongFingerprint, metadata.ContentHashListWithDeterminism, metadata.ContentHashListWithDeterminism));

                        // TODO(jubayard): since we are inside the ContentLocationDatabase, we can validate that all
                        // hashes exist. Moreover, we can prune content.
                    }
                });

            if (!status.Succeeded)
            {
                return new GetContentHashListResult(status.Failure.CreateException());
            }

            if (result is null)
            {
                return new GetContentHashListResult(new ContentHashListWithDeterminism(null, CacheDeterminism.None));
            }

            return new GetContentHashListResult(result.Value);
        }

        /// <summary>
        /// Fine-grained locks that used for all operations that mutate Metadata records.
        /// </summary>
        private readonly object[] _metadataLocks = Enumerable.Range(0, byte.MaxValue + 1).Select(s => new object()).ToArray();

        /// <inheritdoc />
        public override Possible<bool> TryUpsert(
            OperationContext context,
            StrongFingerprint strongFingerprint,
            ContentHashListWithDeterminism replacement,
            Func<MetadataEntry, bool> shouldReplace)
        {
            return _keyValueStore.Use(
                store =>
                {
                    var key = GetMetadataKey(strongFingerprint);

                    lock (_metadataLocks[key[0]])
                    {
                        if (store.TryGetValue(key, out var data, nameof(Columns.Metadata)))
                        {
                            var current = DeserializeMetadataEntry(data);
                            if (!shouldReplace(current))
                            {
                                return false;
                            }
                        }

                        var replacementMetadata = new MetadataEntry(replacement, Clock.UtcNow.ToFileTimeUtc());
                        store.Put(key, SerializeMetadataEntry(replacementMetadata), nameof(Columns.Metadata));
                    }

                    return true;
                });
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
                        // TODO(jubayard): since this method only needs the keys and not the values, it wouldn't hurt
                        // to make an alternative prefix search that doesn't even read the values from RocksDB.
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

        /// <inheritdoc />
        public override Result<IReadOnlyList<Selector>> GetSelectors(OperationContext context, Fingerprint weakFingerprint)
        {
            var selectors = new List<(long TimeUtc, Selector Selector)>();
            var status = _keyValueStore.Use(
                store =>
                {
                    var key = SerializeWeakFingerprint(weakFingerprint);

                    // This only works because the strong fingerprint serializes the weak fingerprint first. Hence,
                    // we know that all keys here are strong fingerprints that match the weak fingerprint.
                    foreach (var kvp in store.PrefixSearch(key, columnFamilyName: nameof(Columns.Metadata)))
                    {
                        var strongFingerprint = DeserializeStrongFingerprint(kvp.Key);
                        var timeUtc = DeserializeMetadataLastAccessTimeUtc(kvp.Value);
                        selectors.Add((timeUtc, strongFingerprint.Selector));
                    }
                });

            if (!status.Succeeded)
            {
                return new Result<IReadOnlyList<Selector>>(status.Failure.CreateException());
            }

            return new Result<IReadOnlyList<Selector>>(selectors
                .OrderByDescending(entry => entry.TimeUtc)
                .Select(entry => entry.Selector).ToList());
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

        private byte[] GetMetadataKey(StrongFingerprint strongFingerprint)
        {
            return SerializeStrongFingerprint(strongFingerprint);
        }

        private byte[] SerializeMetadataEntry(MetadataEntry value)
        {
            return SerializeCore(value, (instance, writer) => instance.Serialize(writer));
        }

        private MetadataEntry DeserializeMetadataEntry(byte[] data)
        {
            return DeserializeCore(data, reader => MetadataEntry.Deserialize(reader));
        }

        private long DeserializeMetadataLastAccessTimeUtc(byte[] data)
        {
            return DeserializeCore(data, reader => MetadataEntry.DeserializeLastAccessTimeUtc(reader));
        }


        private Result<long> GetLongProperty(IBuildXLKeyValueStore store, string propertyName, string columnFamilyName)
        {
            try
            {
                return long.Parse(store.GetProperty(propertyName, columnFamilyName));
            }
            catch (Exception exception)
            {
                return new Result<long>(exception);
            }
        }

        /// <inheritdoc />
        protected override BoolResult GarbageCollectMetadataCore(OperationContext context)
        {
            return _keyValueStore.Use((store, killSwitch) =>
            {
                // The strategy here is to follow what the SQLite memoization store does: we want to keep the top K
                // elements by last access time (i.e. an LRU policy). This is slightly worse than that, because our
                // iterator will go stale as time passes: since we iterate over a snapshot of the DB, we can't
                // guarantee that an entry we remove is truly the one we should be removing. Moreover, since we store
                // information what the last access times were, our internal priority queue may go stale over time as
                // well.
                var liveDbSizeInBytesBeforeGc = GetLongProperty(store, "rocksdb.estimate-live-data-size", columnFamilyName: nameof(Columns.Metadata));

                var scannedEntries = 0;
                var removedEntries = 0;

                using (var cts = CancellationTokenSource.CreateLinkedTokenSource(killSwitch, context.Token))
                {
                    // This is a min-heap using lexicographic order: an element will be at the `Top` if its `fileTimeUtc`
                    // is the smallest (i.e. the oldest). Hence, we always know what the cut-off point is for the top K: if
                    // a new element is smaller than the Top, it's not in the top K, if larger, it is.
                    var entries = new PriorityQueue<(long fileTimeUtc, byte[] strongFingerprint)>(
                        capacity: _configuration.MetadataGarbageCollectionMaximumNumberOfEntriesToKeep + 1,
                        comparer: Comparer<(long fileTimeUtc, byte[] strongFingerprint)>.Create((x, y) => x.fileTimeUtc.CompareTo(y.fileTimeUtc)));
                    foreach (var keyValuePair in store.PrefixSearch((byte[])null, nameof(Columns.Metadata)))
                    {
                        // NOTE(jubayard): the expensive part of this is iterating over the whole database; the less we
                        // take _while_ we do that, the better. An alternative is to compute a quantile sketch and remove
                        // unneeded entries as we go. We could also batch deletions here.

                        if (cts.IsCancellationRequested)
                        {
                            break;
                        }

                        var entry = (fileTimeUtc: DeserializeMetadataLastAccessTimeUtc(keyValuePair.Value),
                            strongFingerprint: keyValuePair.Key);

                        byte[] strongFingerprintToRemove = null;

                        if (entries.Count >= _configuration.MetadataGarbageCollectionMaximumNumberOfEntriesToKeep && entries.Top.fileTimeUtc > entry.fileTimeUtc)
                        {
                            // If we already reached the maximum number of elements to keep, and the current entry is older
                            // than the oldest in the top K, we can just remove the current entry.
                            strongFingerprintToRemove = entry.strongFingerprint;
                        }
                        else
                        {
                            // We either didn't reach the number of elements we want to keep, or the entry has a last
                            // access time larger than the current smallest one in the top K.
                            entries.Push(entry);

                            if (entries.Count > _configuration.MetadataGarbageCollectionMaximumNumberOfEntriesToKeep)
                            {
                                strongFingerprintToRemove = entries.Top.strongFingerprint;
                                entries.Pop();
                            }
                        }

                        if (!(strongFingerprintToRemove is null))
                        {
                            store.Remove(strongFingerprintToRemove, columnFamilyName: nameof(Columns.Metadata));
                            removedEntries++;
                        }

                        scannedEntries++;
                    }
                }

                Counters[ContentLocationDatabaseCounters.GarbageCollectMetadataEntriesRemoved].Add(removedEntries);
                Counters[ContentLocationDatabaseCounters.GarbageCollectMetadataEntriesScanned].Add(scannedEntries);

                var liveDbSizeInBytesAfterGc = GetLongProperty(store, "rocksdb.estimate-live-data-size", columnFamilyName: nameof(Columns.Metadata));

                // NOTE(jubayard): since we report the live DB size, it is possible it may increase after GC, because
                // new tombstones have been added. However, there is no way to compute how much we added/removed that
                // doesn't involve either keeping track of the values, or doing two passes over the column family.
                Tracer.Debug(context, $"Metadata Garbage Collection results: ScannedEntries=[{scannedEntries}] RemovedEntries=[{removedEntries}] LiveDbSizeInBytesBeforeGc=[{liveDbSizeInBytesBeforeGc}] LiveDbSizeInBytesAfterGc=[{liveDbSizeInBytesAfterGc}] KillSwitch=[{killSwitch.IsCancellationRequested}]");

                return Unit.Void;
            }).ToBoolResult();
        }

        /// <inheritdoc />
        public override Result<long> GetContentDatabaseSizeBytes()
        {
            return _keyValueStore.Use(store => long.Parse(store.GetProperty("rocksdb.live-sst-files-size"))).ToResult();
        }

        private void FullRangeCompaction(OperationContext context)
        {
            if (ShutdownStarted)
            {
                return;
            }

            using (var cancellableContext = TrackShutdown(context))
            {
                var ctx = (OperationContext)cancellableContext;
                var killSwitchUsed = false;
                ctx.PerformOperation<BoolResult>(Tracer, () =>
                    PossibleExtensions.ToBoolResult(_keyValueStore.Use((store, killSwitch) =>
                    {
                        using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ctx.Token, killSwitch))
                        {
                            foreach (var columnFamily in new[] { "default", nameof(Columns.ClusterState), nameof(Columns.Metadata) })
                            {
                                if (cts.IsCancellationRequested)
                                {
                                    killSwitchUsed = killSwitch.IsCancellationRequested;
                                    break;
                                }

                                var result = CompactColumnFamily(context, store, columnFamily);
                                if (!result.Succeeded)
                                {
                                    break;
                                }
                            }
                        }
                    })),
                    messageFactory: _ => $"KillSwitch=[{killSwitchUsed}]").IgnoreFailure();
            }

            if (!ShutdownStarted)
            {
                lock (TimerChangeLock)
                {
                    // No try-catch required here.
                    _compactionTimer?.Change(_configuration.FullRangeCompactionInterval, Timeout.InfiniteTimeSpan);
                }
            }
        }

        private BoolResult CompactColumnFamily(OperationContext context, IBuildXLKeyValueStore store, string columnFamilyName)
        {
            uint? startRange = null;
            uint? endRange = null;

            return context.PerformOperation(Tracer, () =>
            {
                switch (_configuration.FullRangeCompactionVariant)
                {
                    case FullRangeCompactionVariant.EntireRange:
                    {
                        store.CompactRange((byte[])null, null, columnFamilyName);
                        break;
                    }
                    case FullRangeCompactionVariant.ByteIncrements:
                    {
                        var info = GetCompactionInfo(store, columnFamilyName);

                        var compactionEndPrefix = unchecked((byte)(info.LastCompactionEndPrefix + _configuration.FullRangeCompactionByteIncrementStep));

                        startRange = info.LastCompactionEndPrefix;
                        endRange = compactionEndPrefix;

                        var start = new byte[] { info.LastCompactionEndPrefix };
                        var limit = new byte[] { compactionEndPrefix };
                        if (info.LastCompactionEndPrefix > compactionEndPrefix)
                        {
                            // We'll wrap around when we add the increment step at the end of the range; hence, we produce two compactions.
                            store.CompactRange(start, null, columnFamilyName);
                            store.CompactRange(null, limit, columnFamilyName);
                        }
                        else
                        {
                            store.CompactRange(start, limit, columnFamilyName);
                        }

                        StoreCompactionInfo(
                            store,
                            new CompactionInfo
                            {
                                LastCompactionEndPrefix = compactionEndPrefix,
                            },
                            columnFamilyName);
                        break;
                    }
                    case FullRangeCompactionVariant.WordIncrements:
                    {
                        var info = GetCompactionInfo(store, columnFamilyName);

                        var compactionEndPrefix = unchecked((ushort)(info.LastWordCompactionEndPrefix + _configuration.FullRangeCompactionByteIncrementStep));

                        startRange = info.LastWordCompactionEndPrefix;
                        endRange = compactionEndPrefix;

                        var start = BitConverter.GetBytes(info.LastWordCompactionEndPrefix);
                        var limit = BitConverter.GetBytes(compactionEndPrefix);
                        if (info.LastWordCompactionEndPrefix > compactionEndPrefix)
                        {
                            // We'll wrap around when we add the increment step at the end of the range; hence, we produce two compactions.
                            store.CompactRange(start, null, columnFamilyName);
                            store.CompactRange(null, limit, columnFamilyName);
                        }
                        else
                        {
                            store.CompactRange(start, limit, columnFamilyName);
                        }

                        StoreCompactionInfo(
                            store,
                            new CompactionInfo
                            {
                                LastWordCompactionEndPrefix = compactionEndPrefix,
                            },
                            columnFamilyName);
                        break;
                    }
                }

                return BoolResult.Success;
            }, messageFactory: _ => $"ColumnFamily=[{columnFamilyName}] Variant=[{_configuration.FullRangeCompactionVariant}] Start=[{startRange?.ToString() ?? "NULL"}] End=[{endRange?.ToString() ?? "NULL"}]");
        }

        private void StoreCompactionInfo(IBuildXLKeyValueStore store, CompactionInfo compactionInfo, string columnFamilyName)
        {
            Contract.RequiresNotNull(store);
            Contract.RequiresNotNullOrEmpty(columnFamilyName);

            var key = Encoding.UTF8.GetBytes($"{columnFamilyName}_CompactionInfoV2");
            var value = SerializeCompactionInfo(compactionInfo);
            store.Put(key, value, columnFamilyName: nameof(Columns.DatabaseManagement));
        }

        private CompactionInfo GetCompactionInfo(IBuildXLKeyValueStore store, string columnFamilyName)
        {
            Contract.RequiresNotNull(store);
            Contract.RequiresNotNullOrEmpty(columnFamilyName);

            var key = Encoding.UTF8.GetBytes($"{columnFamilyName}_CompactionInfoV2");
            if (store.TryGetValue(key, out var value, columnFamilyName: nameof(Columns.DatabaseManagement)))
            {
                return DeserializeCompactionInfo(value);
            }

            return new CompactionInfo();
        }

        private byte[] SerializeCompactionInfo(CompactionInfo strongFingerprint)
        {
            return SerializeCore(strongFingerprint, (instance, writer) => instance.Serialize(writer));
        }

        private CompactionInfo DeserializeCompactionInfo(byte[] bytes)
        {
            return DeserializeCore(bytes, reader => CompactionInfo.Deserialize(reader));
        }

        private struct CompactionInfo
        {
            public byte LastCompactionEndPrefix { get; set; }

            public ushort LastWordCompactionEndPrefix { get; set; }

            public void Serialize(BuildXLWriter writer)
            {
                writer.Write(LastCompactionEndPrefix);
                writer.Write(LastWordCompactionEndPrefix);
            }

            public static CompactionInfo Deserialize(BuildXLReader reader)
            {
                var lastCompactionEndPrefix = reader.ReadByte();
                var lastWordCompactionEndPrefix = reader.ReadUInt16();

                return new CompactionInfo()
                {
                    LastCompactionEndPrefix = lastCompactionEndPrefix,
                    LastWordCompactionEndPrefix = lastWordCompactionEndPrefix,
                };
            }
        }

        private class KeyValueStoreGuard : IDisposable
        {
            private KeyValueStoreAccessor _accessor;

            /// <summary>
            /// The kill switch is used to stop all long running operations. Such operations should call the Use
            /// overload that gets a <see cref="CancellationToken"/>, and re-start the operation from the last valid
            /// state when the kill switch gets triggered.
            ///
            /// Operations that do this will have their database switched under them as they are running. They can
            /// also choose to terminate gracefully if possible. For examples, see:
            ///  - <see cref="GarbageCollectMetadataCore(OperationContext)"/>
            ///  - <see cref="FullRangeCompaction(OperationContext)"/>
            ///  - Content GC
            /// </summary>
            private CancellationTokenSource _killSwitch = new CancellationTokenSource();

            private readonly ReaderWriterLockSlim _accessorLock = new ReaderWriterLockSlim(recursionPolicy: LockRecursionPolicy.SupportsRecursion);

            public KeyValueStoreGuard(KeyValueStoreAccessor accessor)
            {
                _accessor = accessor;
            }

            public void Dispose()
            {
                _killSwitch.Cancel();

                using var token = _accessorLock.AcquireWriteLock();

                _accessor.Dispose();
                _killSwitch.Dispose();
            }

            public void Replace(KeyValueStoreAccessor accessor)
            {
                _killSwitch.Cancel();

                using var token = _accessorLock.AcquireWriteLock();

                _accessor.Dispose();
                _accessor = accessor;

                _killSwitch.Dispose();
                _killSwitch = new CancellationTokenSource();
            }

            public Possible<TResult> Use<TState, TResult>(Func<IBuildXLKeyValueStore, TState, TResult> action, TState state)
            {
                using var token = _accessorLock.AcquireReadLock();
                return _accessor.Use(action, state);
            }

            public Possible<Unit> Use(Action<IBuildXLKeyValueStore> action)
            {
                using var token = _accessorLock.AcquireReadLock();
                return _accessor.Use(action);
            }

            public Possible<TResult> Use<TResult>(Func<IBuildXLKeyValueStore, TResult> action)
            {
                using var token = _accessorLock.AcquireReadLock();
                return _accessor.Use(action);
            }

            public Possible<TResult> Use<TState, TResult>(Func<IBuildXLKeyValueStore, TState, CancellationToken, TResult> action, TState state)
            {
                using var token = _accessorLock.AcquireReadLock();
                return _accessor.Use((store, innerState) => action(store, innerState.state, innerState.token), (state, token: _killSwitch.Token));
            }

            public Possible<Unit> Use(Action<IBuildXLKeyValueStore, CancellationToken> action)
            {
                using var token = _accessorLock.AcquireReadLock();
                return _accessor.Use((store, killSwitch) =>
                {
                    action(store, killSwitch);
                    return Unit.Void;
                }, _killSwitch.Token);
            }

            public Possible<TResult> Use<TResult>(Func<IBuildXLKeyValueStore, CancellationToken, TResult> action)
            {
                using var token = _accessorLock.AcquireReadLock();
                return _accessor.Use((store, killSwitch) => action(store, killSwitch), _killSwitch.Token);
            }
        }
    }
}
