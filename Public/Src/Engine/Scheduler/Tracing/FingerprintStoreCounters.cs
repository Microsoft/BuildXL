// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Tracing;

namespace BuildXL.Scheduler.Tracing
{
    /// <summary>
    /// Counters for fingerprint store infrastructure and logging.
    /// Shared between <see cref="FingerprintStore"/> and <see cref="FingerprintStoreExecutionLogTarget"/>.
    /// </summary>
    public enum FingerprintStoreCounters
    {
        /// <summary>
        /// The total number of <see cref="ProcessFingerprintComputationEventData"/> processed.
        /// </summary>
        NumFingerprintComputationEvents,

        /// <summary>
        /// The total number of <see cref="DirectoryMembershipHashedEventData"/> processed.
        /// </summary>
        NumDirectoryMembershipEvents,

        /// <summary>
        /// The number of <see cref="ProcessFingerprintComputationEventData"/> computed at cache lookup time that are skipped.
        /// Fingerprint computation information for weak fingerprint misses are only recorded at execution time.
        /// </summary>
        /// <note>
        /// This count can include non-cacheable pips.
        /// </note>
        NumCacheLookupFingerprintComputationSkipped,

        /// <summary>
        /// The number of <see cref="ProcessFingerprintComputationEventData"/> stored in the <see cref="FingerprintStoreExecutionLogTarget.CacheLookupFingerprintStore"/>.
        /// To prevent redundancy, only strong fingerprint misses are stored in the cache lookup store. Fingerprint computation information for weak fingerprint misses are only recorded at execution time.
        /// </summary>
        NumCacheLookupFingerprintComputationStored,

        /// <summary>
        /// The number of <see cref="ProcessFingerprintComputationEventData"/> skipped due to
        /// a non-cacheable pip (a pip was executed without calculating a fingerprint for caching).
        /// </summary>
        NumFingerprintComputationSkippedNonCacheablePip,

        /// <summary>
        /// The number of <see cref="ProcessFingerprintComputationEventData"/> skipped due to
        /// an existing entry with the same value.
        /// </summary>
        NumFingerprintComputationSkippedSameValueEntryExists,

        /// <summary>
        /// The number of cache hits that put an entry to the store.
        /// </summary>
        NumHitEntriesPut,

        /// <summary>
        /// The number of pip unique output hash entries put to the fingerprint store.
        /// </summary>
        NumPipUniqueOutputHashEntriesPut,

        /// <summary>
        /// The number of <see cref="ProcessFingerprintComputationEventData"/> that
        /// put to the <see cref="FingerprintStore"/>.
        /// </summary>
        NumPipFingerprintEntriesPut,

        /// <summary>
        /// The number of <see cref="DirectoryMembershipHashedEventData"/> that
        /// put to the <see cref="FingerprintStore"/>.
        /// </summary>
        NumDirectoryMembershipEntriesPut,

        /// <summary>
        /// The number of path set entries that put to the <see cref="FingerprintStore"/>.
        /// </summary>
        NumPathSetEntriesPut,

        /// <summary>
        /// The number of pip unique output hash entries garbage collected during the build.
        /// </summary>
        NumPipUniqueOutputHashEntriesGarbageCollected,

        /// <summary>
        /// The number of pip unique output hash entries remaining in the store at the
        /// end of the build.
        /// </summary>
        NumPipUniqueOutputHashEntriesRemaining,

        /// <summary>
        /// The number of pip fingerprint entries garbage collected during the build.
        /// </summary>
        NumPipFingerprintEntriesGarbageCollected,

        /// <summary>
        /// The number of pip fingerprint entries remaining in the store at the
        /// end of the build.
        /// </summary>
        NumPipFingerprintEntriesRemaining,

        /// <summary>
        /// The number of content hash entries garbage collected during the build.
        /// </summary>
        NumContentHashEntriesGarbageCollected,
        
        /// <summary>
        /// The number of content hash entries remaining in the store at the
        /// end of the build.
        /// </summary>
        NumContentHashEntriesRemaining,

        /// <summary>
        /// The number of storage files hardlinked during snapshot.
        /// </summary>
        SnapshotNumStorageFilesHardlinked,

        /// <summary>
        /// The number of storage files copied-on-write during snapshot.
        /// </summary>
        SnapshotNumStorageFilesCopyOnWrite,

        /// <summary>
        /// The number of log or metadata files copied during snapshot.
        /// </summary>
        SnapshotNumOtherFilesCopied,

        /// <summary>
        /// The total size in bytes of storage files in the EngineCache.
        /// </summary>
        TotalStorageFilesSizeBytes,

        /// <summary>
        /// The total size in bytes of log or metadata files in the EngineCache.
        /// </summary>
        TotalOtherFilesSizeBytes,

        /// <summary>
        /// The average age of pip fingerprint entries in the store, calculated during garbage collection.
        /// </summary>
        PipFingerprintEntriesAverageEntryAgeMinutes,

        /// <summary>
        /// The average age of content hash fingerprint entries in the store, calculated during garbage collection.
        /// </summary>
        ContentHashEntriesAverageEntryAgeMinutes,
        
        /// <summary>
        /// The average age of pip output hash entries in the store, calculated during garbage collection.
        /// </summary>
        PipUniqueOutputHashEntriesAverageEntryAgeMinutes,
        
        /// <summary>
        /// The number of ms spent serializing and storing to the fingerprint store.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        FingerprintStoreLoggingTime,

        /// <summary>
        /// The number of ms spent serializing and storing to the fingerprint store.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        FingerprintStoreAwaitingEventProcessorTime,

        /// <summary>
        /// The number of ms spent serializing fingerprint store entries to JSON.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        JsonSerializationTime,

        /// <summary>
        /// The number of ms spent serializing weak fingerprint to JSON.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        JsonSerializationWeakFingerprintTime,

        /// <summary>
        /// The number of serializing weak fingerprint to JSON.
        /// </summary>
        JsonSerializationWeakFingerprintCount,

        /// <summary>
        /// The number of ms spent serializing strong fingerprint content to JSON.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        JsonSerializationStrongFingerprintContentTime,

        /// <summary>
        /// The number of ms spent serializing strong fingerprint input to JSON.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        JsonSerializationStrongFingerprintInputTime,

        /// <summary>
        /// The number of ms spent serializing input collection to JSON.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        JsonSerializationInputCollectionTime,

        /// <summary>
        /// The number of ms spent updating or storing pip unique's output hash into fingerprint store.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        UpdateOrStorePipUniqueOutputHashEntryTime,

        /// <summary>
        /// The number of ms spent creating fingerpring store entry.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        CreateFingerprintStoreEntryTime,

        /// <summary>
        /// The number of creating fingerprint store entries.
        /// </summary>
        CreateFingerprintStoreEntryCount,

        /// <summary>
        /// The number of ms spent putting fingerprint store entry into fingerprint store.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        PutFingerprintStoreEntryTime,

        /// <summary>
        /// The number of ms spent serializing the cache miss list.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        SerializeCacheMissListTime,

        /// <summary>
        /// The number of ms spent deserializing the cache miss list.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        DeserializeCacheMissListTime,

        /// <summary>
        /// The number of ms spent serializing the LRU entries map used for garbage collect.
        /// This is done in parallel for each column family, but this is the sum of all column families.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        SerializeLruEntriesMapsTime,

        /// <summary>
        /// The number of ms spent deserializing the LRU entries map used for garbage collect.
        /// This is done in parallel for each column family, but this is the sum of all column families.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        DeserializeLruEntriesMapTime,

        /// <summary>
        /// The number of ms spent garbage collecting the fingerprint store and managing LRU overhead.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        TotalGarbageCollectionTime,

        /// <summary>
        /// The number of ms spent garbage collecting the fingerprint store.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        GarbageCollectionTime,

        /// <summary>
        /// The max number of ms spent garbage collecting a single entry in the fingerprint store.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        GarbageCollectionMaxEntryTime,

        /// <summary>
        /// The number of ms spent creating a fingerprint store snapshot for logs.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        SnapshotTime,

        /// <summary>
        /// The number of ms spent for cache miss analysis to find the old entries
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        CacheMissFindOldEntriesTime,

        /// <summary>
        /// The number of ms spent for cache miss analysis.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        CacheMissAnalysisTime,

        /// <summary>
        /// The number of ms spent to load the fingerprint store that will be used for comparison.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        InitializeCacheMissAnalysisDuration,

        /// <summary>
        /// How many pips cache miss analysis from cache look-up is performed on.
        /// </summary>
        CacheMissAnalysisAnalyzeCacheLookUpCount,

        /// <summary>
        /// How many pips cache miss analysis from execution is performed on.
        /// </summary>
        CacheMissAnalysisAnalyzeExecutionCount,

        /// <summary>
        /// How many pips cache miss analysis is performed on during cache-lookup.
        /// </summary>
        CacheMissAnalysisCacheLookupAnalyzeCount,

        /// <summary>
        /// How many pips cache miss analysis can't be performed due to max number reach.
        /// </summary>
        CacheMissAnalysisExceedMaxNumAndCannotPerformCount,

        /// <summary>
        /// How many pips cache miss analysis results are enqueued for batching.
        /// </summary>
        CacheMissBatchingEnqueueCount,

        /// <summary>
        /// How many pips cache miss analysis results are dequeued for batching.
        /// </summary>
        CacheMissBatchingDequeueCount,

        /// <summary>
        /// The number of ms spent for analyzing (e.g., diff-ing fingerprints) cache misses.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        CacheMissAnalysisAnalyzeDuration,

        /// <summary>
        /// The number of ms spent for analysis result batch logging 
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        CacheMissBatchLoggingTime,

        /// <summary>
        /// The number of ms spent for disposing analysis result batch logging queue
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        RuntimeCacheMissBatchLoggingQueueDisposeDuration,

        /// <summary>
        /// The number of ms spent for disposing PreviousFingerprintStore
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        PreviousFingerprintStoreDisposeDuration
    }
}
