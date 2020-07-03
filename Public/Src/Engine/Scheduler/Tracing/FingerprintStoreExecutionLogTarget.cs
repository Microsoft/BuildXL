// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Native.IO;
using BuildXL.Pips;
using BuildXL.Pips.DirectedGraph;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.ParallelAlgorithms;
using BuildXL.Utilities.Tracing;
using static BuildXL.Scheduler.Tracing.FingerprintStore;
using KVP = System.Collections.Generic.KeyValuePair<string, string>;
using PipKVP = System.Collections.Generic.KeyValuePair<string, BuildXL.Scheduler.Tracing.FingerprintStore.PipFingerprintKeys>;

namespace BuildXL.Scheduler.Tracing
{
    /// <summary>
    /// Logging target for sending inputs to fingerprint computation to <see cref="FingerprintStore"/> instances.
    /// Encapsulates the logic for serializing entries for <see cref="FingerprintStore"/> instances.
    /// </summary>
    public sealed class FingerprintStoreExecutionLogTarget : ExecutionLogTargetBase
    {
        /// <summary>
        /// Pip execution context
        /// </summary>
        private readonly PipExecutionContext m_context;

        /// <summary>
        /// Used to hydrate pips from <see cref="PipId"/>s.
        /// </summary>
        private readonly PipTable m_pipTable;

        /// <summary>
        /// Used to collect the inputs used during weak fingerprint computation.
        /// </summary>
        internal readonly PipContentFingerprinter PipContentFingerprinter;

        /// <summary>
        /// Key-value store for storing fingerprint computation data computed at:
        /// 1. Pip execution time
        /// 2. Cache lookup time if there is a cache hit (data from cache hits should be representative of the data that would have come from executing the pip)
        /// </summary>
        internal readonly FingerprintStore ExecutionFingerprintStore;

        /// <summary>
        /// Key-value store for storing fingerprint computation data computed at:
        /// 1. Cache lookup time if there is a strong fingerprint mismatch
        /// 
        /// To prevent storing redudant data, only strong fingerprint mismatches are stored. The weak fingerprint of a pip composed entirely of static information
        /// and will not change throughout the build, so the weak fingerprint stored in the <see cref="ExecutionFingerprintStore"/> at pip execution time is sufficient.
        /// </summary>
        internal readonly FingerprintStore CacheLookupFingerprintStore;

        /// <summary>
        /// Maintains the order of cache misses seen in a build.
        /// </summary>
        private readonly ConcurrentQueue<PipCacheMissInfo> m_pipCacheMissesQueue;

        /// <summary>
        /// Whether the <see cref="Tracing.FingerprintStore"/> should be garbage collected during dispose.
        /// </summary>
        private bool m_fingerprintComputedForExecution = false;

        /// <summary>
        /// Store fingerprint inputs from workers in distributed builds.
        /// </summary>
        public override bool CanHandleWorkerEvents => true;

        private IConfiguration m_configuration { get;  }

        /// <summary>
        /// Counters, shared with <see cref="Tracing.FingerprintStore"/>.
        /// </summary>
        public CounterCollection<FingerprintStoreCounters> Counters { get; }

        /// <summary>
        /// Context for logging methods.
        /// </summary>
        public LoggingContext LoggingContext { get; }

        private readonly Task<RuntimeCacheMissAnalyzer> m_runtimeCacheMissAnalyzerTask;

        private RuntimeCacheMissAnalyzer RuntimeCacheMissAnalyzer => m_runtimeCacheMissAnalyzerTask.GetAwaiter().GetResult();

        private bool CacheMissAnalysisEnabled => RuntimeCacheMissAnalyzer != null;

        private bool CacheLookupStoreEnabled => CacheLookupFingerprintStore != null && !CacheLookupFingerprintStore.Disabled;

        private readonly FingerprintStoreEventProcessor m_fingerprintStoreEventProcessor;

        /// <summary>
        /// Cache for weak fingerprint serialization.
        /// </summary>
        /// <remarks>
        /// Weak fingerprint serialization can be very expensive when pip specification is huge. When pip has a cache miss, one can
        /// possibly compute the weak fingerprint twice, one of cache look-up event (if cache look-up store is enabled), and the other
        /// for execution event. This cache is a transient cache as its entry is short lived, i.e., we only cache the weak fingerprint serialization
        /// for cache look-up event, and remove it, if any, when the execution event is processed.
        /// 
        /// We do not cache strong fingerprint computations because, when cache miss, the cache look-up one and the execution one are different.
        /// </remarks>
        private readonly ConcurrentDictionary<PipId, string> m_weakFingerprintSerializationTransientCache;

        /// <summary>
        /// Mappings from augmented weak fingerprints to original weak fingerprints.
        /// </summary>
        /// <remarks>
        /// Fingerprint store will only stores pip's original weak fingerprints. Thus, we need a mapping from the augmented ones to the original ones.
        /// </remarks>
        private readonly ConcurrentDictionary<(PipId, WeakContentFingerprint), WeakContentFingerprint> m_augmentedWeakFingerprintsToOriginalWeakFingeprints;

        private bool m_disposed = false;

        /// <summary>
        /// Creates a <see cref="FingerprintStoreExecutionLogTarget"/>.
        /// </summary>
        /// <returns>
        /// If successful, a <see cref="FingerprintStoreExecutionLogTarget"/> that logs to
        /// a <see cref="Tracing.FingerprintStore"/> at the provided directory;
        /// otherwise, null.
        /// </returns>
        public static FingerprintStoreExecutionLogTarget Create(
            PipExecutionContext context,
            PipTable pipTable,
            PipContentFingerprinter pipContentFingerprinter,
            LoggingContext loggingContext,
            IConfiguration configuration,
            EngineCache cache,
            IReadonlyDirectedGraph graph,
            CounterCollection<FingerprintStoreCounters> counters,
            IDictionary<PipId, RunnablePipPerformanceInfo> runnablePipPerformance = null,
            FingerprintStoreTestHooks testHooks = null)
        {
            var fingerprintStorePathString = configuration.Layout.FingerprintStoreDirectory.ToString(context.PathTable);
            var cacheLookupFingerprintStorePathString = configuration.Logging.CacheLookupFingerprintStoreLogDirectory.ToString(context.PathTable);

            var maxEntryAge = new TimeSpan(hours: 0, minutes: configuration.Logging.FingerprintStoreMaxEntryAgeMinutes, seconds: 0);

            // Most operations performed on the execution fingerprint store are writes.
            // Speed up writes by opening the fingerprint store with bulk load; see https://github.com/facebook/rocksdb/wiki/RocksDB-FAQ
            var possibleExecutionStore = FingerprintStore.Open(
                fingerprintStorePathString,
                bulkLoad: configuration.Logging.FingerprintStoreBulkLoad,
                maxEntryAge: maxEntryAge,
                mode: configuration.Logging.FingerprintStoreMode,
                loggingContext: loggingContext,
                counters: counters,
                testHooks: testHooks);

            Possible<FingerprintStore> possibleCacheLookupStore = new Failure<string>("No attempt to create a cache lookup fingerprint store yet.");
            if (configuration.Logging.FingerprintStoreMode != FingerprintStoreMode.ExecutionFingerprintsOnly)
            {
                // Most operations performed on the execution fingerprint store are writes.
                // Speed up writes by opening the fingerprint store with bulk load; see https://github.com/facebook/rocksdb/wiki/RocksDB-FAQ
                possibleCacheLookupStore = FingerprintStore.Open(
                    cacheLookupFingerprintStorePathString,
                    bulkLoad: configuration.Logging.FingerprintStoreBulkLoad,
                    maxEntryAge: maxEntryAge,
                    mode: configuration.Logging.FingerprintStoreMode,
                    loggingContext: loggingContext,
                    counters: counters,
                    testHooks: testHooks);
            }

            if (possibleExecutionStore.Succeeded
                && (possibleCacheLookupStore.Succeeded || configuration.Logging.FingerprintStoreMode == FingerprintStoreMode.ExecutionFingerprintsOnly))
            {
                return new FingerprintStoreExecutionLogTarget(
                    loggingContext,
                    context,
                    pipTable,
                    pipContentFingerprinter,
                    possibleExecutionStore.Result,
                    possibleCacheLookupStore.Succeeded ? possibleCacheLookupStore.Result : null,
                    configuration,
                    cache,
                    graph,
                    counters,
                    runnablePipPerformance,
                    testHooks: testHooks);
            }
            else
            {
                if (!possibleExecutionStore.Succeeded)
                {
                    Logger.Log.FingerprintStoreUnableToOpen(loggingContext, possibleExecutionStore.Failure.DescribeIncludingInnerFailures());
                }
                
                if (!possibleCacheLookupStore.Succeeded)
                {
                    Logger.Log.FingerprintStoreUnableToOpen(loggingContext, possibleCacheLookupStore.Failure.DescribeIncludingInnerFailures());
                }
            }

            return null;
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        private FingerprintStoreExecutionLogTarget(
            LoggingContext loggingContext,
            PipExecutionContext context,
            PipTable pipTable,
            PipContentFingerprinter pipContentFingerprinter,
            FingerprintStore fingerprintStore,
            FingerprintStore cacheLookupFingerprintStore,
            IConfiguration configuration,
            EngineCache cache,
            IReadonlyDirectedGraph graph,
            CounterCollection<FingerprintStoreCounters> counters,
            IDictionary<PipId, RunnablePipPerformanceInfo> runnablePipPerformance,
            FingerprintStoreTestHooks testHooks = null)
        {
            m_context = context;
            m_pipTable = pipTable;
            LoggingContext = loggingContext;
            PipContentFingerprinter = pipContentFingerprinter;
            ExecutionFingerprintStore = fingerprintStore;
            CacheLookupFingerprintStore = cacheLookupFingerprintStore;
            // Cache lookup store is per-build state and doesn't need to be garbage collected (vs. execution fignerprint store which is persisted build-over-build)
            CacheLookupFingerprintStore?.GarbageCollectCancellationToken.Cancel();
            m_pipCacheMissesQueue = new ConcurrentQueue<PipCacheMissInfo>();
            Counters = counters;
            m_configuration = configuration;
            m_runtimeCacheMissAnalyzerTask = RuntimeCacheMissAnalyzer.TryCreateAsync(
                this,
                loggingContext,
                context,
                configuration,
                cache,
                graph,
                runnablePipPerformance,
                testHooks: testHooks);

            m_fingerprintStoreEventProcessor = new FingerprintStoreEventProcessor(Environment.ProcessorCount);
            m_weakFingerprintSerializationTransientCache = new ConcurrentDictionary<PipId, string>();
            m_augmentedWeakFingerprintsToOriginalWeakFingeprints = new ConcurrentDictionary<(PipId, WeakContentFingerprint), WeakContentFingerprint>();

            Contract.Assume(
                m_configuration.Logging.FingerprintStoreMode == FingerprintStoreMode.ExecutionFingerprintsOnly || CacheLookupFingerprintStore != null, 
                "Unless /storeFingerprints flag is set to /storeFingerprints:ExecutionFingerprintsOnly, the cache lookup FingerprintStore must exist.");
        }

        /// <summary>
        /// For now the fingerprint store doesn't care about workerId,
        /// so just use the same object instead of making a new object with the same
        /// underlying store.
        /// </summary>
        public override IExecutionLogTarget CreateWorkerTarget(uint workerId) => this;

        /// <summary>
        /// Adds an entry to the fingerprint store for { directory fingerprint : directory fingerprint inputs }.
        /// </summary>
        public override void DirectoryMembershipHashed(DirectoryMembershipHashedEventData data)
        {
            if (ExecutionFingerprintStore.Disabled)
            {
                return;
            }

            using (Counters.StartStopwatch(FingerprintStoreCounters.FingerprintStoreLoggingTime))
            {
                Counters.IncrementCounter(FingerprintStoreCounters.NumDirectoryMembershipEvents);

                var stringContentHash = ContentHashToString(data.DirectoryFingerprint.Hash);
                if (!ExecutionFingerprintStore.TryGetContentHashValue(stringContentHash, out var value ))
                {
                    Counters.IncrementCounter(FingerprintStoreCounters.NumDirectoryMembershipEntriesPut);
                    ExecutionFingerprintStore.PutContentHash(stringContentHash, JsonSerialize(data));
                }
            }
        }

        /// <summary>
        /// Selects the most relevant strong fingerprint computation event to use.
        /// </summary>
        private ProcessStrongFingerprintComputationData? SelectStrongFingerprintComputationData(ProcessFingerprintComputationEventData data)
        {
            var numStrongFingerprints = data.StrongFingerprintComputations.Count;

            if (numStrongFingerprints == 0)
            {
                return null;
            }

            // There are two cases when fingerprints are put into the store:
            //
            // Case 1: If processing a strong fingerprint computed for cache lookup (cache hit, cache misses are ignored until execution), 
            // cache lookup automatically stops on the strong fingerprint match, so the last strong fingerprint is the fingerprint used
            //
            // Case 2: If processing a strong fingerprint computed for execution (cache miss), 
            // there should only be one strong fingerprint, so the last strong fingerprint is the fingerprint used
            //
            // Find the last strong fingerprint data that has no augmented weak fingerprint. Those that have augmented weak fingerprints
            // are either part of publishing the augmented weak fingerprints (not used for cache look-up), or have marker as their strong fingeprints.
            int chosenStrongFingerprintDataIndex = numStrongFingerprints - 1;
            for (; chosenStrongFingerprintDataIndex >= 0; --chosenStrongFingerprintDataIndex)
            {
                if (!data.StrongFingerprintComputations[chosenStrongFingerprintDataIndex].AugmentedWeakFingerprint.HasValue)
                {
                    break;
                }
            }

            if (chosenStrongFingerprintDataIndex < 0)
            {
                return null;
            }

            return data.StrongFingerprintComputations[chosenStrongFingerprintDataIndex];
        }

        /// <summary>
        /// Maps pip's augmented weak fingerprints to its original weak fingerprint.
        /// </summary>
        private void MapAugmentedWeakFingerprints(ProcessFingerprintComputationEventData data)
        {
            foreach (var sFpComputation in data.StrongFingerprintComputations)
            {
                if (sFpComputation.AugmentedWeakFingerprint.HasValue)
                {
                    // If the fingerprint computation is part of the publish augmented weak fingerprint, then the computed strong fingerprint is indeed the augmented weak fingerprint.
                    Contract.Assert(
                        !sFpComputation.IsNewlyPublishedAugmentedWeakFingerprint 
                        || sFpComputation.AugmentedWeakFingerprint.Value == new WeakContentFingerprint(sFpComputation.ComputedStrongFingerprint.Hash));

                    m_augmentedWeakFingerprintsToOriginalWeakFingeprints.TryAdd(
                        (data.PipId, sFpComputation.AugmentedWeakFingerprint.Value),
                        data.WeakFingerprint);
                }
            }
        }

        /// <summary>
        /// Helper functions for putting entries into the fingerprint store for sub-components.
        /// { pip formatted semi stable hash : weak fingerprint, strong fingerprint, path set hash }
        /// { weak fingerprint hash : weak fingerprint inputs }
        /// { strong fingerprint hash : strong fingerprint inputs }
        /// { path set hash : path set inputs }
        /// </summary>
        private FingerprintStoreEntry CreateAndStoreFingerprintStoreEntry(
            FingerprintStore fingerprintStore,
            Process pip,
            PipFingerprintKeys pipFingerprintKeys,
            WeakContentFingerprint weakFingerprint,
            ProcessStrongFingerprintComputationData strongFingerprintData,
            bool cacheWeakFingerprintSerialization = false)
        {
            // If we got this far, a new pip is being put in the store
            Counters.IncrementCounter(FingerprintStoreCounters.NumPipFingerprintEntriesPut);

            UpdateOrStorePipUniqueOutputHashEntry(fingerprintStore, pip);

            // A content hash-keyed entry will have the same value as long as the key is the same, so overwriting it is unnecessary
            var mustStorePathEntry = !fingerprintStore.ContainsContentHash(pipFingerprintKeys.FormattedPathSetHash) || CacheMissAnalysisEnabled;

            var entry = CreateFingerprintStoreEntry(
                pip,
                pipFingerprintKeys,
                weakFingerprint,
                strongFingerprintData,
                mustStorePathEntry: mustStorePathEntry,
                cacheWeakFingerprintSerialization: cacheWeakFingerprintSerialization);

            fingerprintStore.PutFingerprintStoreEntry(entry, storePathSet: mustStorePathEntry);
            return entry;
        }

        internal FingerprintStoreEntry CreateFingerprintStoreEntry(
            Process pip,
            PipFingerprintKeys pipFingerprintKeys,
            WeakContentFingerprint weakFingerprint,
            ProcessStrongFingerprintComputationData strongFingerprintData,
            bool mustStorePathEntry = true,
            bool cacheWeakFingerprintSerialization = false)
        {
            Counters.IncrementCounter(FingerprintStoreCounters.CreateFingerprintStoreEntryCount);

            using (Counters.StartStopwatch(FingerprintStoreCounters.CreateFingerprintStoreEntryTime))
            {
                string pipSerializedWeakFingerprint = null;

                if (cacheWeakFingerprintSerialization)
                {
                    pipSerializedWeakFingerprint = m_weakFingerprintSerializationTransientCache.GetOrAdd(
                        pip.PipId,
                        (pipId, p) =>
                        {
                            Counters.IncrementCounter(FingerprintStoreCounters.JsonSerializationWeakFingerprintCount);
                            return JsonSerialize(p);
                        },
                        pip);
                }
                else
                {
                    if (!m_weakFingerprintSerializationTransientCache.TryRemove(pip.PipId, out pipSerializedWeakFingerprint))
                    {
                        Counters.IncrementCounter(FingerprintStoreCounters.JsonSerializationWeakFingerprintCount);
                        pipSerializedWeakFingerprint = JsonSerialize(pip);
                    }
                }

                return new FingerprintStoreEntry
                {
                    // { pip formatted semi stable hash : weak fingerprint, strong fingerprint, path set hash }
                    PipToFingerprintKeys = new PipKVP(pip.FormattedSemiStableHash, pipFingerprintKeys),
                    // { weak fingerprint hash : weak fingerprint inputs }
                    WeakFingerprintToInputs = new KVP(pipFingerprintKeys.WeakFingerprint, pipSerializedWeakFingerprint),
                    StrongFingerprintEntry = new StrongFingerprintEntry
                    {
                        // { strong fingerprint hash: strong fingerprint inputs }
                        StrongFingerprintToInputs = new KVP(pipFingerprintKeys.StrongFingerprint, JsonSerialize(weakFingerprint, strongFingerprintData.PathSetHash, strongFingerprintData.ObservedInputs)),
                        // { path set hash : path set inputs }
                        // If fingerprint comparison is enabled, the entry should contain the pathset json.
                        PathSetHashToInputs = mustStorePathEntry ? new KVP(pipFingerprintKeys.FormattedPathSetHash, JsonSerialize(strongFingerprintData)) : default,
                    }
                };
            }
        }

        /// <summary>
        /// Puts <see cref="FingerprintStoreEntry"/> to the <see cref="ExecutionFingerprintStore"/> if the same entry does not exist.
        /// </summary>
        /// <returns>
        /// True, if the entry was added; false if the entry already exists.
        /// </returns>
        private bool TryPutExecutionFingerprintStoreEntry(Process pip, WeakContentFingerprint weakFingerprint, ProcessStrongFingerprintComputationData strongFingerprintData, string sessionId, string relatedSessionId)
        {
            var strongFingerprint = strongFingerprintData.ComputedStrongFingerprint;

            // Use the session id and related session id of this build to create pipFingerprintKeys, so when we need to CreateAndStoreFingerprintStoreEntry, they will be in the new entry.
            // The SameValueEntryExists function below doesn't check sessionId and relatedSessionId, so the value of them do not affect the result of SameValueEntryExists()
            var pipFingerprintKeys = new PipFingerprintKeys(weakFingerprint, strongFingerprint, ContentHashToString(strongFingerprintData.PathSetHash), sessionId, relatedSessionId);

            // Skip overwriting the same value on cache hits
            if (SameValueEntryExists(ExecutionFingerprintStore, pip, pipFingerprintKeys))
            {
                // No fingerprint entry needs to be stored for this pip, but it's unique output hash entry might need to be updated
                UpdateOrStorePipUniqueOutputHashEntry(ExecutionFingerprintStore, pip);
                Counters.IncrementCounter(FingerprintStoreCounters.NumFingerprintComputationSkippedSameValueEntryExists);
                return false;
            }
            else
            {
                CreateAndStoreFingerprintStoreEntry(ExecutionFingerprintStore, pip, pipFingerprintKeys, weakFingerprint, strongFingerprintData);
                Counters.IncrementCounter(FingerprintStoreCounters.NumHitEntriesPut);
                return true;
            }
        }

        /// <summary>
        /// Processes a fingerprint computed for cache lookup.
        /// This will put or overwrite an entry in the <see cref="ExecutionFingerprintStore"/>
        /// if two conditions are met:
        /// 1. The fingerprint computed has a strong fingerprint match from the cache.
        /// 2. The fingerprint computed does not already exist with the same value in the fingerprint store.
        /// 
        /// This will put an entry in the <see cref="CacheLookupFingerprintStore"/> for every strong fingerprint cache miss.
        /// </summary>
        private void ProcessFingerprintComputedForCacheLookup(ProcessFingerprintComputationEventData data)
        {
            var maybeStrongFingerprintData = SelectStrongFingerprintComputationData(data);

            if (maybeStrongFingerprintData == null)
            {
                // Weak fingerprint miss, relevant fingerprint information will be recorded at execution time
                Counters.IncrementCounter(FingerprintStoreCounters.NumCacheLookupFingerprintComputationSkipped);
                return;
            }
            
            var strongFingerprintData = maybeStrongFingerprintData.Value;
            if (!strongFingerprintData.Succeeded)
            {
                // Something went wrong when computing the strong fingerprint
                // Don't bother attempting to store or analyze data since the data may be partial and cause failures
                Counters.IncrementCounter(FingerprintStoreCounters.NumCacheLookupFingerprintComputationSkipped);
                return;
            }

            // Maps augmented weak fingerprints, if any, to the original one.
            MapAugmentedWeakFingerprints(data);

            var pip = GetProcess(data.PipId);
            var weakFingerprint = GetOriginalWeakFingerprint(data);

            // Cache hit, update execution fingerprint store entry, if necessary, to match what would have been executed
            if (strongFingerprintData.IsStrongFingerprintHit)
            {
                TryPutExecutionFingerprintStoreEntry(pip, weakFingerprint, maybeStrongFingerprintData.Value, data.SessionId, data.RelatedSessionId);
            }
            // Strong fingerprint cache miss, store the most relevant fingerprint to the cache lookup fingerprint store
            else if (CacheLookupStoreEnabled || CacheMissAnalysisEnabled)
            {
                bool shouldAnalyzeMiss = false;
                
                if (tryGetMoreRelevantStrongFingerprintData(out var relevantStrongFingerprintData))
                {
                    strongFingerprintData = relevantStrongFingerprintData;

                    // Only perform analysis if there is a relevant strong fingerprint data.
                    // Otherwise fall back to the analysis after execution.
                    shouldAnalyzeMiss = true;
                }

                var strongFingerprint = strongFingerprintData.ComputedStrongFingerprint;
                var pipFingerprintKeys = new PipFingerprintKeys(
                    weakFingerprint,
                    strongFingerprint,
                    ContentHashToString(strongFingerprintData.PathSetHash),
                    LoggingContext.Session.Id,
                    LoggingContext.Session.RelatedId);

                FingerprintStoreEntry newEntry = null;

                if (CacheLookupStoreEnabled)
                {
                    // All directory membership fingerprint entries are stored in the ExecutionFingerprintStore as they are computed during the build
                    // Copy any necessary directory membership fingerprint entries to the CacheLookupStore on a need-to-have basis
                    foreach (var input in strongFingerprintData.ObservedInputs)
                    {
                        if (input.PathEntry.DirectoryEnumeration)
                        {
                            var hashKey = ContentHashToString(input.Hash);
                            if (ExecutionFingerprintStore.TryGetContentHashValue(hashKey, out var directoryMembership))
                            {
                                CacheLookupFingerprintStore.PutContentHash(hashKey, directoryMembership);
                            }
                        }
                    }

                    Counters.IncrementCounter(FingerprintStoreCounters.NumCacheLookupFingerprintComputationStored);
                    newEntry = CreateAndStoreFingerprintStoreEntry(CacheLookupFingerprintStore, pip, pipFingerprintKeys, weakFingerprint, strongFingerprintData, cacheWeakFingerprintSerialization: true);
                }

                if (shouldAnalyzeMiss)
                {
                    newEntry ??= CreateFingerprintStoreEntry(pip, pipFingerprintKeys, weakFingerprint, strongFingerprintData, cacheWeakFingerprintSerialization: true);

                    // Strong fingerprint misses need to be analyzed during cache-lookup to get a precise reason.
                    RuntimeCacheMissAnalyzer?.AnalyzeForCacheLookup(newEntry, pip);
                }
            }

            // Try to get more relevant strong fingerprint data based on previous fingerprint store.
            // The fingerprint is relevant if it has the same weak fingerprint and path set hash.
            // Find the strong fingerprint from the cache that matches the path set stored in the previous fingerprint store.
            bool tryGetMoreRelevantStrongFingerprintData(out ProcessStrongFingerprintComputationData sFpData)
            {
                sFpData = default;

                if (!CacheMissAnalysisEnabled)
                {
                    // If cache miss analysis is not enabled, then there is no previous fingerprint store.
                    return false;
                }

                // Look up the previous fingerprint store, and consider relevant strong fingerprint data if it comes from the same weak fingerprint.
                if (RuntimeCacheMissAnalyzer.PreviousFingerprintStore.TryGetPipFingerprintKeys(pip.FormattedSemiStableHash, out var fpKeys)
                    && fpKeys.WeakFingerprint == weakFingerprint.ToString())
                {
                    foreach (var sFpComputation in data.StrongFingerprintComputations)
                    {
                        // During cache look-up, we analyze path set, but never say that the cache miss is due to path set mismatched.
                        // Thus, for more relevant strong fingeprint data, we find the one that has a matching path set.
                        // Ignore the ones with augmented weak fingerprint because either they are part of publishing the augmented weak fingeprints (not used during cache look-up)
                        // or theiry associated strong fingerprint is just a marker.
                        if (sFpComputation.Succeeded
                            && !sFpComputation.AugmentedWeakFingerprint.HasValue
                            && fpKeys.FormattedPathSetHash == ContentHashToString(sFpComputation.PathSetHash))
                        {
                            sFpData = sFpComputation;
                            return true;
                        }
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// Processes a fingerprint computed for execution. This will always put or overwrite an entry in the 
        /// fingerprint store.
        /// </summary>
        private void ProcessFingerprintComputedForExecution(ProcessFingerprintComputationEventData data)
        {
            m_fingerprintComputedForExecution = true;

            var maybeStrongFingerprintData = SelectStrongFingerprintComputationData(data);
            FingerprintStoreEntry newEntry = null;
            Process pip = GetProcess(data.PipId);

            if (maybeStrongFingerprintData == null)
            {
                // If an executed pip doesn't have a fingerprint computation, don't put it in the fingerprint store
                Counters.IncrementCounter(FingerprintStoreCounters.NumFingerprintComputationSkippedNonCacheablePip);
            }
            else
            {
                var strongFingerprintData = maybeStrongFingerprintData.Value;
                var weakFingerprint = GetOriginalWeakFingerprint(data);
                var strongFingerprint = strongFingerprintData.ComputedStrongFingerprint;
                var pipFingerprintKeys = new PipFingerprintKeys(weakFingerprint, strongFingerprint, ContentHashToString(strongFingerprintData.PathSetHash), LoggingContext.Session.Id, LoggingContext.Session.RelatedId);
                newEntry = CreateAndStoreFingerprintStoreEntry(ExecutionFingerprintStore, pip, pipFingerprintKeys, weakFingerprint, strongFingerprintData);
            }

            RuntimeCacheMissAnalyzer?.AnalyzeForExecution(newEntry, pip);
        }

        /// <summary>
        /// Stores fingerprint computation information once per pip:
        /// If cache hit, store info for the fingerprint match calculated during cache lookup.
        /// If cache miss, store info for the fingerprint calculated at execution time.
        /// 
        /// Adds entries to the fingerprint store for:
        /// { pip semistable hash : weak and strong fingerprint hashes }
        /// { weak fingerprint hash : weak fingerprint inputs }
        /// { strong fingerprint hash : strong fingerprint inputs }
        /// { path set hash : path set inputs }
        /// </summary>
        public override void ProcessFingerprintComputed(ProcessFingerprintComputationEventData data)
        {
            if (ExecutionFingerprintStore.Disabled)
            {
                return;
            }

            m_fingerprintStoreEventProcessor.Enqueue(data.PipId, () => ProcessFingerprintComputationData(data));
        }

        /// <summary>
        /// Aggregate a list of the pip cache misses to write to the store at the end of the build.
        /// </summary>
        public override void PipCacheMiss(PipCacheMissEventData data)
        {
            if (ExecutionFingerprintStore.Disabled)
            {
                return;
            }

            // Unlike ProcessFingerprintComputationEventData, processing PipCacheMissEventData is cheap and synchronous
            // and thus we still maintain the proper order needed by runtime cache miss analysis, i.e.,
            // we know that fingerprints will be processed afterwards.
            ProcessCacheMissData(data);
        }

        private void ProcessFingerprintComputationData(ProcessFingerprintComputationEventData data)
        {
            using (Counters.StartStopwatch(FingerprintStoreCounters.FingerprintStoreLoggingTime))
            {
                Counters.IncrementCounter(FingerprintStoreCounters.NumFingerprintComputationEvents);

                if (data.Kind == FingerprintComputationKind.CacheCheck)
                {
                    ProcessFingerprintComputedForCacheLookup(data);
                }
                else
                {
                    ProcessFingerprintComputedForExecution(data);
                }
            }
        }

        private void ProcessCacheMissData(PipCacheMissEventData data)
        {
            using (Counters.StartStopwatch(FingerprintStoreCounters.FingerprintStoreLoggingTime))
            {
                var cacheMissInfo = new PipCacheMissInfo
                {
                    PipId = data.PipId,
                    CacheMissType = data.CacheMissType,
                    MissedOutputs = data.MissedOutputs,
                };

                m_pipCacheMissesQueue.Enqueue(cacheMissInfo);
                RuntimeCacheMissAnalyzer?.AddCacheMiss(cacheMissInfo);
            }
        }

        /// <summary>
        /// Checks if an entry for the pip with the same fingerprints already exists in the store.
        /// </summary>
        private bool SameValueEntryExists(FingerprintStore fingerprintStore, Process pip, PipFingerprintKeys newKeys)
        {
            var keyFound = fingerprintStore.TryGetPipFingerprintKeys(pip.FormattedSemiStableHash, out PipFingerprintKeys oldKeys);
            return keyFound 
                && oldKeys.WeakFingerprint == newKeys.WeakFingerprint
                && oldKeys.StrongFingerprint == newKeys.StrongFingerprint;
        }

        /// <summary>
        /// Updates the pip unique output hash entry in the fingerprint store to match the pip in the current build.
        /// 
        /// The pip unique output hash is more stable that the pip formatted semi stable hash. If it can be computed
        /// and does not already exist, store an entry to act as the primary lookup key.
        /// </summary>
        private void UpdateOrStorePipUniqueOutputHashEntry(FingerprintStore fingerprintStore, Process pip)
        {
            using (Counters.StartStopwatch(FingerprintStoreCounters.UpdateOrStorePipUniqueOutputHashEntryTime))
            {
                if (pip.TryComputePipUniqueOutputHash(m_context.PathTable, out var outputHash, PipContentFingerprinter.PathExpander))
                {
                    var entryExists = fingerprintStore.TryGetPipUniqueOutputHashValue(outputHash.ToString(), out var oldSemiStableHash);
                    if (!entryExists // missing
                        || (entryExists && oldSemiStableHash != pip.FormattedSemiStableHash)) // out-of-date
                    {
                        Counters.IncrementCounter(FingerprintStoreCounters.NumPipUniqueOutputHashEntriesPut);
                        fingerprintStore.PutPipUniqueOutputHash(outputHash, pip.FormattedSemiStableHash);
                    }
                }
            }
        }

        /// <summary>
        /// Serializes the value to JSON for { weak fingerprint hash : weak fingerprint inputs }.
        /// </summary>
        private string JsonSerialize(Process pip)
        {
            using (Counters.StartStopwatch(FingerprintStoreCounters.JsonSerializationWeakFingerprintTime))
            {
                return JsonSerializeHelper((writer) =>
                {
                    // Use same logic as fingerprint computation
                    PipContentFingerprinter.AddWeakFingerprint(writer, pip);
                },
                pathExpander: PipContentFingerprinter.PathExpander);
            }
        }

        /// <summary>
        /// Serializes the value to JSON for { strong fingerprint hash : strong fingerprint inputs }.
        /// </summary>
        private string JsonSerialize(WeakContentFingerprint weakFingerprint, ContentHash pathSetHash, ReadOnlyArray<ObservedInput> observedInputs)
        {
            using (Counters.StartStopwatch(FingerprintStoreCounters.JsonSerializationStrongFingerprintContentTime))
            {
                return JsonSerializeHelper((writer) =>
                {
                    // Use same logic as fingerprint computation
                    ObservedInputProcessingResult.AddStrongFingerprintContent(writer, weakFingerprint, pathSetHash, observedInputs);
                });
            }
        }

        /// <summary>
        /// Serializes <see cref="ProcessStrongFingerprintComputationData"/> to JSON, including the path set.
        /// </summary>
        private string JsonSerialize(ProcessStrongFingerprintComputationData data)
        {
            using (Counters.StartStopwatch(FingerprintStoreCounters.JsonSerializationStrongFingerprintInputTime))
            {
                return JsonSerializeHelper((writer) =>
                {
                    data.WriteFingerprintInputs(writer);
                },
                pathExpander: PipContentFingerprinter.PathExpander);
            }
        }

        /// <summary>
        /// Serializes <see cref="IFingerprintInputCollection"/> to JSON.
        /// </summary>
        private string JsonSerialize(IFingerprintInputCollection data)
        {
            using (Counters.StartStopwatch(FingerprintStoreCounters.JsonSerializationInputCollectionTime))
            {
                return JsonSerializeHelper((writer) =>
                {
                    data.WriteFingerprintInputs(writer);
                });
            }
        }

        /// <summary>
        /// Hydrates a pip from <see cref="PipId"/>. The pip will still be in-memory at call time.
        /// </summary>
        internal Process GetProcess(PipId pipId) => (Process)m_pipTable.HydratePip(pipId, PipQueryContext.FingerprintStore);

        /// <summary>
        /// Convenience wrapper converting JSON fingerprinting ops to string.
        /// </summary>
        private string JsonSerializeHelper(Action<JsonFingerprinter> fingerprintOps, PathExpander pathExpander = null)
        {
            using (Counters.StartStopwatch(FingerprintStoreCounters.JsonSerializationTime))
            {
                return JsonFingerprinter.CreateJsonString(fingerprintOps, pathTable: m_context.PathTable, pathExpander: pathExpander);
            }
        }

        /// <summary>
        /// Converts a hash to a string. This should be kept in-sync with <see cref="JsonFingerprinter"/> to allow <see cref="FingerprintStore"/> look-ups
        /// using content hashes parsed from JSON.
        /// </summary>
        internal static string ContentHashToString(ContentHash hash) => JsonFingerprinter.ContentHashToString(hash);

        private WeakContentFingerprint GetOriginalWeakFingerprint(ProcessFingerprintComputationEventData data) => 
            m_augmentedWeakFingerprintsToOriginalWeakFingeprints.TryGetValue((data.PipId, data.WeakFingerprint), out var originalWeakFingerprint) 
            ? originalWeakFingerprint
            : data.WeakFingerprint;

        /// <inheritdoc />
        public override void Dispose()
        {
            if (m_disposed)
            {
                return;
            }

            using (Counters.StartStopwatch(FingerprintStoreCounters.FingerprintStoreLoggingTime))
            {
                // Store the ordered pip cache miss list as one blob
                ExecutionFingerprintStore.PutCacheMissList(m_pipCacheMissesQueue.ToList());

                using (Counters.StartStopwatch(FingerprintStoreCounters.FingerprintStoreAwaitingEventProcessorTime))
                {
                    m_fingerprintStoreEventProcessor.Complete();
                }

                // We should first dispose the fingerprintStore in the RunCacheMissAnalyzer
                // because that might be the snapshot version of FingerprintStore
                // in case cache miss analysis is in the local-mode.
                RuntimeCacheMissAnalyzer?.Dispose();

                // For performance, cancel garbage collect for builds with no cache misses
                if (!m_fingerprintComputedForExecution)
                {
                    ExecutionFingerprintStore.GarbageCollectCancellationToken.Cancel();
                }

                ExecutionFingerprintStore.Dispose();
                CacheLookupFingerprintStore?.Dispose();

                if (!m_configuration.Logging.SaveFingerprintStoreToLogs.GetValueOrDefault())
                {
                    FileUtilities.DeleteDirectoryContents(m_configuration.Logging.FingerprintsLogDirectory.ToString(m_context.PathTable), true);
                }
            }

            base.Dispose();

            m_disposed = true;
        }

        private class FingerprintStoreEventProcessor
        {
            private readonly ActionBlockSlim<Action>[] m_actionBlocks;

            public FingerprintStoreEventProcessor(int degreeOfParallelism)
            {
                Contract.Requires(degreeOfParallelism > 0);

                // To ensure that events coming from the same pips are processed according to the order when they came,
                // we use N action blocks, all with degree of parallelism 1. Thus, we potentially get parallelism across
                // different pips, but maintain sequential processing within a single pip.
                // This is necessary in particular when the runtime cache miss analyzer is enabled because 
                // we use cachelookup fingerprints to perform cache miss analyzer for strongfingerprint misses.
                m_actionBlocks = new ActionBlockSlim<Action>[degreeOfParallelism];
                for (int i = 0; i < degreeOfParallelism; ++i)
                {
                    m_actionBlocks[i] = new ActionBlockSlim<Action>(1, a => a());
                }
            }

            public void Enqueue(PipId pipId, Action action)
            {
                m_actionBlocks[Math.Abs(pipId.Value % m_actionBlocks.Length)].Post(action);
            }

            public void Complete()
            {
                for (int i = 0; i < m_actionBlocks.Length; ++i)
                {
                    m_actionBlocks[i].Complete();
                }

                for (int i = 0; i < m_actionBlocks.Length; ++i)
                {
                    m_actionBlocks[i].CompletionAsync().Wait();
                }
            }
        }
    }
}
