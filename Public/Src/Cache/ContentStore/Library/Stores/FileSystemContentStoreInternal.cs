// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BuildXL.Cache.ContentStore.Exceptions;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.ContentStore.Synchronization;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.UtilitiesCore.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;
using FileInfo = BuildXL.Cache.ContentStore.Interfaces.FileSystem.FileInfo;
using RelativePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.RelativePath;

#nullable enable

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    ///     Content addressable store where content is stored on disk
    /// </summary>
    /// <remarks>
    ///     CacheRoot               C:\blah\Cache
    ///     CacheShareRoot          C:\blah\Cache\Shared          \\machine\CAS
    ///     CacheContentRoot        C:\blah\Cache\Shared\SHA1
    ///     ContentHashRoot    C:\blah\Cache\Shared\SHA1\abc
    /// </remarks>
    public class FileSystemContentStoreInternal : StartupShutdownBase, IContentStoreInternal, IContentDirectoryHost
    {
        /// <summary>
        ///     Public name for monitoring use.
        /// </summary>
        public const string Component = "FileSystemContentStore";

        private const string CurrentByteCountName = Component + ".CurrentByteCount";
        private const string CurrentFileCountName = Component + ".CurrentFileCount";

        // TODO: Adjust defaults (bug 1365340)
        private const int ParallelPlaceFilesLimit = 8;

        private const string BlobNameExtension = "blob";

        /// <summary>
        ///     Directory to write temp files in.
        /// </summary>
        private const string TempFileSubdirectory = "temp";

        /// <summary>
        ///     Length of subdirectory names used for storing files. For example with length 3,
        ///     content with hash "abcdefg" will be stored in $root\abc\abcdefg.blob.
        /// </summary>
        internal const int HashDirectoryNameLength = 3;

        /// <nodoc />
        protected IAbsFileSystem FileSystem { get; }

        /// <nodoc />
        protected IClock Clock { get; }

        /// <nodoc />
        public AbsolutePath RootPath { get; }

        /// <inheritdoc />
        protected override Tracer Tracer => _tracer;

        private readonly IDistributedLocationStore? _distributedStore;

        private readonly ContentStoreInternalTracer _tracer;

        private readonly ConfigurationModel _configurationModel;
        private readonly CounterCollection<Counter> _counters = new CounterCollection<Counter>();

        private readonly AbsolutePath _contentRootDirectory;
        private readonly AbsolutePath _tempFolder;

        /// <summary>
        ///     LockSet used to ensure thread safety on write operations.
        /// </summary>
        private readonly LockSet<ContentHash> _lockSet = new LockSet<ContentHash>();

        private bool _applyDenyWriteAttributesOnContent;

        private IContentChangeAnnouncer? _announcer;

        /// <summary>
        ///     List of cached files and their metadata.
        /// </summary>
        protected internal readonly IContentDirectory ContentDirectory;

        /// <summary>
        ///     Tracker for the number of times each content has been pinned.
        /// </summary>
        protected readonly ConcurrentDictionary<ContentHash, Pin> PinMap = new ConcurrentDictionary<ContentHash, Pin>();

        /// <summary>
        ///     Tracker for the index of the most recent successful hardlinked replica for each hash.
        /// </summary>
        private readonly ConcurrentDictionary<ContentHash, int> _replicaCursors = new ConcurrentDictionary<ContentHash, int>();

        /// <summary>
        /// Stream containing the empty file.
        /// </summary>
        private readonly StreamWithLength _emptyFileStream = new NonClosingEmptyMemoryStream();

        /// <summary>
        ///     Cumulative count of instances of the content directory being discovered as out of sync with the disk.
        /// </summary>
        private long _contentDirectoryMismatchCount;

        // Fields and properties that initialized in StartupCoreAsync
        /// <nodoc />
        public ContentStoreConfiguration? Configuration { get; private set; }

        /// <summary>
        /// Controls concurrency of shutting down quota keeper in a thread-safe manner
        /// </summary>
        private readonly SemaphoreSlim _stopEvictionGate = TaskUtilities.CreateMutex();

        /// <nodoc />
        protected QuotaKeeper? QuotaKeeper;

        private BackgroundTaskTracker? _taskTracker;

        private PinSizeHistory? _pinSizeHistory;

        private int _pinContextCount;

        private long _maxPinSize;

        private readonly ContentStoreSettings _settings;

        private readonly FileSystemContentStoreInternalChecker _checker;

        /// <nodoc />
        public FileSystemContentStoreInternal(
            IAbsFileSystem fileSystem,
            IClock clock,
            AbsolutePath rootPath,
            ConfigurationModel? configurationModel = null,
            ContentStoreSettings? settings = null,
            IDistributedLocationStore? distributedStore = null)
        {
            Contract.Requires(fileSystem != null);
            Contract.Requires(clock != null);

            _distributedStore = distributedStore;

            _tracer = new ContentStoreInternalTracer(settings);
            int maxContentPathLengthRelativeToCacheRoot = GetMaxContentPathLengthRelativeToCacheRoot();

            RootPath = rootPath;
            if ((RootPath.Path.Length + 1 + maxContentPathLengthRelativeToCacheRoot) >= FileSystemConstants.MaxPath)
            {
                throw new CacheException("Root path does not provide enough room for cache paths to fit MAX_PATH");
            }

            Clock = clock;
            FileSystem = fileSystem;
            _configurationModel = configurationModel ?? new ConfigurationModel();

            _contentRootDirectory = RootPath / Constants.SharedDirectoryName;
            _tempFolder = _contentRootDirectory / TempFileSubdirectory;

            // MemoryContentDirectory requires for the root path to exist. Making sure this is the case.
            FileSystem.CreateDirectory(RootPath);
            ContentDirectory = new MemoryContentDirectory(FileSystem, RootPath, this);

            _pinContextCount = 0;
            _maxPinSize = -1;

            _settings = settings ?? ContentStoreSettings.DefaultSettings;

            _checker = new FileSystemContentStoreInternalChecker(FileSystem, Clock, RootPath, _tracer, _settings.SelfCheckSettings, this);
        }

        /// <summary>
        /// Checks that the content on disk is correct and every file in content directory matches it's hash.
        /// </summary>
        /// <returns></returns>
        public async Task<Result<SelfCheckResult>> SelfCheckContentDirectoryAsync(Context context, CancellationToken token)
        {
            using (var disposableContext = TrackShutdown(context, token))
            {
                return await _checker.SelfCheckContentDirectoryAsync(disposableContext.Context);
            }
        }

        /// <summary>
        /// Removes invalid content from cache.
        /// </summary>
        internal async Task RemoveInvalidContentAsync(OperationContext context, ContentHash contentHash)
        {
            Contract.Assert(QuotaKeeper != null);
            // In order to remove the content we have to do the following things:
            // Remove file from disk
            // Update memory content directory
            // Update quota keeper
            // Notify distributed store that the content is gone from this machine
            // The first 3 things are happening in the first call.
            await EvictCoreAsync(
                    context,
                    new ContentHashWithLastAccessTimeAndReplicaCount(contentHash, Clock.UtcNow),
                    force: true, // Need to evict an invalid content even if it is pinned.
                    onlyUnlinked: false,
                    size => { QuotaKeeper.OnContentEvicted(size); })
                .TraceIfFailure(context);


            if (_distributedStore != null)
            {
                await _distributedStore.UnregisterAsync(context, new ContentHash[] {contentHash}, context.Token)
                    .TraceIfFailure(context);

            }
        }

        internal async Task<ContentHashWithSize?> TryHashFileAsync(Context context, AbsolutePath path, HashType hashType, Func<Stream, Stream>? wrapStream = null)
        {
            // We only hash the file if a trusted hash is not supplied
            using var stream = await FileSystem.OpenAsync(path, FileAccess.Read, FileMode.Open, FileShare.Read | FileShare.Delete);
            if (stream == null)
            {
                return null;
            }

            long length = stream.Value.Length;

            using var wrappedStream = (wrapStream == null) ? stream : wrapStream(stream);
            Contract.Assert(wrappedStream.CanSeek);
            Contract.Assert(wrappedStream.Length == length);

            // Hash the file in  place
            return await HashContentAsync(context, wrappedStream.AssertHasLength(), hashType, path);
        }

        private void DeleteTempFolder()
        {
            if (FileSystem.DirectoryExists(_tempFolder))
            {
                FileSystem.DeleteDirectory(_tempFolder, DeleteOptions.All);
            }
        }

        /// <inheritdoc />
        public ContentDirectorySnapshot<ContentFileInfo> Reconstruct(Context context)
        {
            // NOTE: DO NOT call ContentDirectory from this method as this is called during the initialization of ContentDirectory and calls
            // into ContentDirectory would cause a deadlock.

            return _tracer.Reconstruct(
                context,
                (stopwatch) =>
                {
                    long contentCount = 0;
                    long contentSize = 0;

                    var contentHashes = ReadSnapshotFromDisk(context);
                    _tracer.Debug(context, $"Enumerated {contentHashes.Count} entries in {stopwatch.ElapsedMilliseconds}ms.");

                    // We are using a list of classes instead of structs due to the maximum object size restriction
                    // When the contents on disk grow large, a list of structs surpasses the limit and forces OOM
                    var hashInfoPairs = new ContentDirectorySnapshot<ContentFileInfo>();
                    foreach (var grouping in contentHashes.GroupByHash())
                    {
                        var contentFileInfo = new ContentFileInfo(Clock, grouping.First().Payload.Length, grouping.Count());
                        contentCount++;
                        contentSize += contentFileInfo.TotalSize;

                        hashInfoPairs.Add(new PayloadFromDisk<ContentFileInfo>(grouping.Key, contentFileInfo));
                    }

                    return (hashInfoPairs, contentCount, contentSize);
                }).contentInfo;
        }

        /// <inheritdoc />
        public IContentChangeAnnouncer? Announcer
        {
            get { return _announcer; }

            set
            {
                Contract.Assert(_announcer == null);
                _announcer = value;
            }
        }

        private async Task<(ContentStoreConfiguration configuration, bool configFileExists)> CreateConfigurationAsync()
        {
            if (_configurationModel.Selection == ConfigurationSelection.RequireAndUseInProcessConfiguration)
            {
                if (_configurationModel.InProcessConfiguration == null)
                {
                    throw new CacheException("In-process configuration selected but it is null");
                }

                return (configuration: _configurationModel.InProcessConfiguration, configFileExists: false);
            }

            if (_configurationModel.Selection == ConfigurationSelection.UseFileAllowingInProcessFallback)
            {
                var readConfigResult = await FileSystem.ReadContentStoreConfigurationAsync(RootPath);

                if (readConfigResult.Succeeded)
                {
                    return (readConfigResult.Data!, configFileExists: true);
                }
                else if (_configurationModel.InProcessConfiguration != null)
                {
                    return (_configurationModel.InProcessConfiguration, configFileExists: false);
                }
                else
                {
                    throw new CacheException($"{nameof(ContentStoreConfiguration)} is missing");
                }
            }

            throw new CacheException($"Invalid {nameof(ConfigurationSelection)}={_configurationModel.Selection}");
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            bool configFileExists;
            (Configuration, configFileExists) = await CreateConfigurationAsync();

            if (!configFileExists && _configurationModel.MissingFileOption == MissingConfigurationFileOption.WriteOnlyIfNotExists)
            {
                await Configuration.Write(FileSystem, RootPath);
            }

            _tracer.Debug(context, $"{nameof(ContentStoreConfiguration)}: {Configuration}");

            _applyDenyWriteAttributesOnContent = Configuration.DenyWriteAttributesOnContent == DenyWriteAttributesOnContentSetting.Enable;

            DeleteTempFolder();
            FileSystem.CreateDirectory(_tempFolder);

            await ContentDirectory.StartupAsync(context).ThrowIfFailure();

            var contentDirectoryCount = await ContentDirectory.GetCountAsync();
            if (contentDirectoryCount != 0 && !FileSystem.DirectoryExists(_contentRootDirectory))
            {
                return new BoolResult(
                    $"Content root directory {_contentRootDirectory} is missing despite CAS metadata indicating {contentDirectoryCount} files.");
            }

            var size = await ContentDirectory.GetSizeAsync();

            _pinSizeHistory =
                await
                    PinSizeHistory.LoadOrCreateNewAsync(
                        FileSystem,
                        Clock,
                        RootPath,
                        Configuration.HistoryBufferSize);

            var quotaKeeperConfiguration = QuotaKeeperConfiguration.Create(Configuration, size);
            QuotaKeeper = QuotaKeeper.Create(
                FileSystem,
                _tracer,
                ShutdownStartedCancellationToken,
                this,
                _distributedStore,
                quotaKeeperConfiguration);

            var result = await QuotaKeeper.StartupAsync(context);

            _taskTracker = new BackgroundTaskTracker(Component, context.CreateNested(nameof(FileSystemContentStoreInternal)));

            _tracer.StartStats(context, size, contentDirectoryCount);

            if (_settings.SelfCheckSettings?.StartSelfCheckInStartup == true)
            {
                // Starting the self check and ignore and trace the failure.
                // Self check procedure is a long running operation that can take longer then an average process lifetime.
                // So instead of relying on timers to recheck content directory, we rely on
                // periodic service restarts.
                SelfCheckContentDirectoryAsync(context.CreateNested(nameof(FileSystemContentStoreInternal)), context.Token).FireAndForget(context);
            }

            return result;
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            var result = BoolResult.Success;

            var statsResult = await GetStatsAsync(context);

            result &= await ShutdownEvictionAsync(context);

            if (_pinSizeHistory != null)
            {
                await _pinSizeHistory.SaveAsync(FileSystem);
            }

            if (_taskTracker != null)
            {
                await _taskTracker.Synchronize();
                await _taskTracker.ShutdownAsync(context);
            }

            result &= await ContentDirectory.ShutdownAsync(context);

            if (_contentDirectoryMismatchCount > 0)
            {
                _tracer.Warning(
                    context,
                    $"Corrected {_contentDirectoryMismatchCount} mismatches between cache blobs and content directory metadata.");
            }

            CleanupTempFolderAtShutdown(context);

            if (statsResult)
            {
                _tracer.TraceStatisticsAtShutdown(context, statsResult.CounterSet, prefix: "FileSystemContentStoreStats");
            }

            return result;
        }

        private void CleanupTempFolderAtShutdown(Context context)
        {
            if (FileSystem.DirectoryExists(_tempFolder))
            {
                foreach (FileInfo fileInfo in FileSystem.EnumerateFiles(_tempFolder, EnumerateOptions.Recurse))
                {
                    try
                    {
                        ForceDeleteFile(fileInfo.FullPath);
                        _tracer.Warning(context, $"Temp file still existed at shutdown: {fileInfo.FullPath}");
                    }
                    catch (IOException ioException)
                    {
                        _tracer.Warning(context, $"Could not clean up temp file due to exception: {ioException}");
                    }
                }
            }
        }

        private static bool ShouldAttemptHardLink(AbsolutePath contentPath, FileAccessMode accessMode, FileRealizationMode realizationMode)
        {
            return contentPath.IsLocal && accessMode == FileAccessMode.ReadOnly &&
                   (realizationMode == FileRealizationMode.Any ||
                    realizationMode == FileRealizationMode.HardLink);
        }

        private bool TryCreateHardlink(
            Context context,
            AbsolutePath source,
            AbsolutePath destination,
            FileRealizationMode realizationMode,
            bool replaceExisting,
            out CreateHardLinkResult hardLinkResult)
        {
            var result = CreateHardLinkResult.Unknown;
            var stopwatch = Stopwatch.StartNew();

            try
            {
                result = FileSystem.CreateHardLink(source, destination, replaceExisting);
                Contract.Assert((result == CreateHardLinkResult.FailedDestinationExists).Implies(!replaceExisting));

                var resultAcceptable = false;
                switch (result)
                {
                    case CreateHardLinkResult.Success:
                    case CreateHardLinkResult.FailedDestinationExists:
                    case CreateHardLinkResult.FailedMaxHardLinkLimitReached:
                    case CreateHardLinkResult.FailedSourceDoesNotExist:
                    case CreateHardLinkResult.FailedAccessDenied:
                    case CreateHardLinkResult.FailedSourceHandleInvalid:
                        resultAcceptable = true;
                        break;

                    case CreateHardLinkResult.FailedNotSupported:
                    case CreateHardLinkResult.FailedSourceAndDestinationOnDifferentVolumes:
                        resultAcceptable = realizationMode != FileRealizationMode.HardLink;
                        break;
                }

                if (!resultAcceptable)
                {
                    throw new CacheException("Failed to create hard link from [{0}] to [{1}]: {2}", source, destination, result);
                }

                hardLinkResult = result;
                return hardLinkResult == CreateHardLinkResult.Success;
            }
            finally
            {
                stopwatch.Stop();
                _tracer.CreateHardLink(context, result, source, destination, realizationMode, replaceExisting, stopwatch.Elapsed);
            }
        }

        /// <inheritdoc />
        public Task<PutResult> PutFileAsync(
            Context context, AbsolutePath path, FileRealizationMode realizationMode, ContentHash contentHash, PinRequest? pinRequest)
        {
            return PutFileImplAsync(context, path, realizationMode, contentHash, pinRequest);
        }

        private async Task<PutResult?> TryPutFileFastAsync(
            Context context, 
            AbsolutePath path, 
            FileRealizationMode realizationMode, 
            ContentHash contentHash, 
            PinRequest? pinRequest, 
            bool shouldAttemptHardLink)
        {
            // Fast path:
            // If hardlinking existing content which has already been pinned in this context
            // just quickly attempt to hardlink from and existing replica
            if (shouldAttemptHardLink
                && ContentDirectory.TryGetFileInfo(contentHash, out var fileInfo)
                && IsPinned(contentHash, pinRequest)
                && _settings.UseRedundantPutFileShortcut)
            {
                using (_counters[Counter.PutFileFast].Start())
                {
                    CheckPinned(contentHash, pinRequest);
                    fileInfo.UpdateLastAccessed(Clock);
                    var placeLinkResult = await PlaceLinkFromCacheAsync(
                                        context,
                                        path,
                                        FileReplacementMode.ReplaceExisting,
                                        realizationMode,
                                        contentHash,
                                        fileInfo,
                                        fastPath: true);

                    if (placeLinkResult == CreateHardLinkResult.Success)
                    {
                        return new PutResult(contentHash, fileInfo.FileSize, contentAlreadyExistsInCache: true)
                        {
                            Diagnostics = "FastPath"
                        };
                    }
                }
            }

            return null;
        }

        private Task<PutResult> PutFileImplAsync(
            Context context, AbsolutePath path, FileRealizationMode realizationMode, ContentHash contentHash, PinRequest? pinRequest, Func<Stream, Stream>? wrapStream = null)
        {
            return _tracer.PutFileAsync(OperationContext(context), path, realizationMode, contentHash, trustedHash: false, async () =>
            {
                bool shouldAttemptHardLink = ShouldAttemptHardLink(path, FileAccessMode.ReadOnly, realizationMode);

                var putResult = await TryPutFileFastAsync(context, path, realizationMode, contentHash, pinRequest, shouldAttemptHardLink);
                if (putResult != null)
                {
                    return putResult;
                }

                using (LockSet<ContentHash>.LockHandle contentHashHandle = await _lockSet.AcquireAsync(contentHash))
                {
                    CheckPinned(contentHash, pinRequest);
                    long contentSize = await GetContentSizeInternalAsync(context, contentHash, pinRequest?.PinContext);
                    if (contentSize >= 0)
                    {
                        // The user provided a hash for content that we already have. Try to satisfy the request without hashing the given file.
                        bool putInternalSucceeded;
                        bool contentExistsInCache;
                        if (shouldAttemptHardLink)
                        {
                            (putInternalSucceeded, contentExistsInCache) = await PutContentInternalAsync(
                                context,
                                contentHash,
                                contentSize,
                                pinRequest?.PinContext,
                                onContentAlreadyInCache: async (hashHandle, primaryPath, info) =>
                                {
                                    var r = await PlaceLinkFromCacheAsync(
                                        context,
                                        path,
                                        FileReplacementMode.ReplaceExisting,
                                        realizationMode,
                                        contentHash,
                                        info);
                                    return r == CreateHardLinkResult.Success;
                                },
                                onContentNotInCache: primaryPath => Task.FromResult(false),
                                announceAddOnSuccess: false);
                        }
                        else
                        {
                            (putInternalSucceeded, contentExistsInCache) = await PutContentInternalAsync(
                                context,
                                contentHash,
                                contentSize,
                                pinRequest?.PinContext,
                                onContentAlreadyInCache: (hashHandle, primaryPath, info) => Task.FromResult(true),
                                onContentNotInCache: primaryPath => Task.FromResult(false));
                        }

                        if (putInternalSucceeded)
                        {
                            return new PutResult(contentHash, contentSize, contentExistsInCache)
                                .WithLockAcquisitionDuration(contentHashHandle);
                        }
                    }
                }

                // Calling PutFileImplNoTraceAsync to avoid double tracing of the operation.
                var result = await PutFileImplNoTraceAsync(context, path, realizationMode, contentHash.HashType, pinRequest, trustedHashWithSize: null, wrapStream);

                if (realizationMode != FileRealizationMode.CopyNoVerify && result.ContentHash != contentHash && result.Succeeded)
                {
                    return new PutResult(result.ContentHash, $"Content at {path} had actual content hash {result.ContentHash.ToShortString()} and did not match expected value of {contentHash.ToShortString()}");
                }

                return result;
            });
        }

        /// <inheritdoc />
        public Task<PutResult> PutFileAsync(
            Context context, AbsolutePath path, FileRealizationMode realizationMode, HashType hashType, PinRequest? pinRequest)
        {
            return PutFileImplAsync(context, path, realizationMode, hashType, pinRequest, trustedHashWithSize: null);
        }

        private Task<PutResult> PutFileImplAsync(
            Context context, AbsolutePath path, FileRealizationMode realizationMode, HashType hashType, PinRequest? pinRequest, ContentHashWithSize? trustedHashWithSize, Func<Stream, Stream>? wrapStream = null)
        {
            return _tracer.PutFileAsync(OperationContext(context), path, realizationMode, hashType, trustedHash: trustedHashWithSize != null,
                () => PutFileImplNoTraceAsync(context, path, realizationMode, hashType, pinRequest, trustedHashWithSize, wrapStream));
        }

        private async Task<PutResult> PutFileImplNoTraceAsync(
            Context context, AbsolutePath path, FileRealizationMode realizationMode, HashType hashType, PinRequest? pinRequest, ContentHashWithSize? trustedHashWithSize, Func<Stream, Stream>? wrapStream = null)
        {
            Contract.Requires(trustedHashWithSize == null || trustedHashWithSize.Value.Size >= 0);

            ContentHashWithSize content = trustedHashWithSize ?? default;
            if (trustedHashWithSize == null)
            {
                // We only hash the file if a trusted hash is not supplied
                var possibleContent = await TryHashFileAsync(context, path, hashType, wrapStream);
                if (possibleContent == null)
                {
                    return new PutResult(default(ContentHash), $"Source file not found at '{path}'.");
                }

                content = possibleContent.Value;
            }

            // If we are given the empty file, the put is a no-op.
            // We have dedicated logic for pinning and returning without having
            // the empty file in the cache directory.
            if (content.Hash.IsEmptyHash())
            {
                return new PutResult(content.Hash, 0L, contentAlreadyExistsInCache: true);
            }

            bool shouldAttemptHardLink = ShouldAttemptHardLink(path, FileAccessMode.ReadOnly, realizationMode);
            var putResult = await TryPutFileFastAsync(context, path, realizationMode, content.Hash, pinRequest, shouldAttemptHardLink);
            if (putResult != null)
            {
                return putResult;
            }

            using (LockSet<ContentHash>.LockHandle contentHashHandle = await _lockSet.AcquireAsync(content.Hash))
            {
                CheckPinned(content.Hash, pinRequest);
                var stopwatch = new Stopwatch();

                if (shouldAttemptHardLink)
                {
                    (bool putInternalSucceeded, bool contentExistsInCache) = await PutContentInternalAsync(
                        context,
                        content.Hash,
                        content.Size,
                        pinRequest?.PinContext,
                        onContentAlreadyInCache: async (hashHandle, primaryPath, info) =>
                                                 {
                                                     // The content exists in the cache. Try to replace the file that is being put in
                                                     // with a link to the file that is already in the cache. Release the handle to
                                                     // allow for the hardlink to succeed.
                                                     using var trace = _tracer.PutFileExistingHardLink();
                                                     // ReSharper disable once AccessToDisposedClosure
                                                     var result = await PlaceLinkFromCacheAsync(
                                                         context,
                                                         path,
                                                         FileReplacementMode.ReplaceExisting,
                                                         realizationMode,
                                                         content.Hash,
                                                         info);
                                                     return result == CreateHardLinkResult.Success;
                                                 },
                        onContentNotInCache: primaryPath =>
                                             {
                                                 using var trace = _tracer.PutFileNewHardLink();
                                                 ApplyPermissions(context, path, FileAccessMode.ReadOnly);

                                                 var hardLinkResult = CreateHardLinkResult.Unknown;
                                                 Func<bool> tryCreateHardlinkFunc = () => TryCreateHardlink(
                                                                                        context,
                                                                                        path,
                                                                                        primaryPath,
                                                                                        realizationMode,
                                                                                        false,
                                                                                        out hardLinkResult);

                                                 bool result = tryCreateHardlinkFunc();
                                                 if (hardLinkResult == CreateHardLinkResult.FailedDestinationExists)
                                                 {
                                                     // Extraneous blobs on disk. Delete them and retry.
                                                     RemoveAllReplicasFromDiskFor(context, content.Hash);
                                                     result = tryCreateHardlinkFunc();
                                                 }

                                                 return Task.FromResult(result);
                                             },
                        announceAddOnSuccess: false);

                    if (putInternalSucceeded)
                    {
                        return new PutResult(content.Hash, content.Size, contentExistsInCache)
                            .WithLockAcquisitionDuration(contentHashHandle);
                    }
                }

                bool alreadyInCache = true;
                // If hard linking failed or wasn't attempted, fall back to copy.
                stopwatch = new Stopwatch();
                await PutContentInternalAsync(
                    context,
                    content.Hash,
                    content.Size,
                    pinRequest?.PinContext,
                    onContentAlreadyInCache: (hashHandle, primaryPath, info) => Task.FromResult(true),
                    onContentNotInCache: async primaryPath =>
                                         {
                                             using var trace = _tracer.PutFileNewCopy();
                                             alreadyInCache = false;

                                             await RetryOnUnexpectedReplicaAsync(
                                                 context,
                                                 () =>
                                                 {
                                                     if (realizationMode == FileRealizationMode.Move)
                                                     {
                                                         return Task.Run(() => FileSystem.MoveFile(path, primaryPath, replaceExisting: false));
                                                     }
                                                     else
                                                     {
                                                         return SafeCopyFileAsync(
                                                             context,
                                                             content.Hash,
                                                             path,
                                                             primaryPath,
                                                             FileReplacementMode.FailIfExists);
                                                     }
                                                 },
                                                 content.Hash,
                                                 expectedReplicaCount: 0);
                                             return true;
                                         });

                return new PutResult(content.Hash, content.Size, contentAlreadyExistsInCache: alreadyInCache)
                    .WithLockAcquisitionDuration(contentHashHandle);
            }
        }

        /// <inheritdoc />
        public Task<PutResult> PutTrustedFileAsync(Context context, AbsolutePath path, FileRealizationMode realizationMode, ContentHashWithSize contentHashWithSize, PinRequest? pinContext = null)
        {
            return PutFileImplAsync(context, path, realizationMode, contentHashWithSize.Hash.HashType, pinContext, trustedHashWithSize: contentHashWithSize);
        }

        /// <inheritdoc />
        public Task<GetStatsResult> GetStatsAsync(Context context)
        {
            return _tracer.GetStatsAsync(
                OperationContext(context),
                async () =>
                {
                    var counters = new CounterSet();
                    counters.Merge(_tracer.GetCounters(), $"{Component}.");
                    counters.Merge(_counters.ToCounterSet(), $"{Component}.");
                    counters.Add($"{Component}.LockWaitMs", (long)_lockSet.TotalLockWaitTime.TotalMilliseconds);

                    if (StartupCompleted)
                    {
                        if (QuotaKeeper != null)
                        {
                            counters.Add($"{CurrentByteCountName}", QuotaKeeper.CurrentSize);

                            counters.Merge(QuotaKeeper.Counters.ToCounterSet());
                        }

                        counters.Add($"{CurrentFileCountName}", await ContentDirectory.GetCountAsync());
                        counters.Merge(ContentDirectory.GetCounters(), "ContentDirectory.");
                    }
                    return new GetStatsResult(counters);
                });
        }

        /// <inheritdoc />
        public Task<bool> Validate(Context context)
        {
            return new FileSystemContentStoreValidator(Tracer, FileSystem, _applyDenyWriteAttributesOnContent, ContentDirectory, Clock, EnumerateBlobPathsFromDisk)
                .ValidateAsync(context);
        }

        /// <summary>
        /// Shutdown of quota keeper to prevent further eviction of content.
        /// Method is thread safe.
        /// </summary>
        public async Task<BoolResult> ShutdownEvictionAsync(Context context)
        {
            using (await _stopEvictionGate.AcquireAsync())
            {
                if (QuotaKeeper != null && !QuotaKeeper.ShutdownCompleted)
                {
                    _tracer.EndStats(context, QuotaKeeper.CurrentSize, await ContentDirectory.GetCountAsync());

                    // NOTE: QuotaKeeper must be shut down before the content directory because it owns
                    // background operations which may be calling EvictAsync or GetLruOrderedContentListAsync
                    return await QuotaKeeper.ShutdownAsync(context);
                }

                return BoolResult.Success;
            }
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<ContentInfo>> EnumerateContentInfoAsync()
        {
            return ContentDirectory.EnumerateContentInfoAsync();
        }

        /// <inheritdoc />
        public Task<IEnumerable<ContentHash>> EnumerateContentHashesAsync()
        {
            return ContentDirectory.EnumerateContentHashesAsync();
        }

        /// <summary>
        ///     Complete all pending/background operations.
        /// </summary>
        public async Task SyncAsync(Context context, bool purge = true)
        {
            Contract.Assert(QuotaKeeper != null);
            await QuotaKeeper.SyncAsync(context, purge);

            // Ensure there are no pending LRU updates.
            await ContentDirectory.SyncAsync();
        }

        /// <inheritdoc />
        public PinSizeHistory.ReadHistoryResult ReadPinSizeHistory(int windowSize)
        {
            Contract.Assert(_pinSizeHistory != null);
            return _pinSizeHistory.ReadHistory(windowSize);
        }

        /// <summary>
        ///     Protected implementation of Dispose pattern.
        /// </summary>
        protected override void DisposeCore()
        {
            base.DisposeCore();

            QuotaKeeper?.Dispose();
            _taskTracker?.Dispose();
            ContentDirectory.Dispose();
        }

        /// <summary>
        ///     Called by PutContentInternalAsync when the content already exists in the cache.
        /// </summary>
        /// <returns>True if the callback is successful.</returns>
        private delegate Task<bool> OnContentAlreadyExistsInCache(
            ContentHash contentHash, AbsolutePath primaryPath, ContentFileInfo info);

        /// <summary>
        ///     Called by PutContentInternalAsync when the content already exists in the cache.
        /// </summary>
        /// <returns>True if the callback is successful.</returns>
        private delegate Task<bool> OnContentNotInCache(AbsolutePath primaryPath);

        private async Task<(bool Success, bool ContentAlreadyExistsInCache)> PutContentInternalAsync(
            Context context,
            ContentHash contentHash,
            long contentSize,
            PinContext? pinContext,
            OnContentAlreadyExistsInCache onContentAlreadyInCache,
            OnContentNotInCache onContentNotInCache,
            bool announceAddOnSuccess = true)
        {
            Contract.Assert(QuotaKeeper != null);

            AbsolutePath primaryPath = GetPrimaryPathFor(contentHash);
            bool failed = false;
            bool contentExistsInCache = false;
            long addedContentSize = 0;

            using var trace = _tracer.PutContentInternal();

            await ContentDirectory.UpdateAsync(contentHash, touch: true, Clock, async fileInfo =>
            {
                if (fileInfo == null || await RemoveEntryIfNotOnDiskAsync(context, contentHash))
                {
                    using (var txn = await QuotaKeeper.ReserveAsync(contentSize))
                    {
                        FileSystem.CreateDirectory(primaryPath.GetParent());

                        if (!await onContentNotInCache(primaryPath))
                        {
                            failed = true;
                            return null;
                        }

                        txn.Commit();
                        PinContentIfContext(contentHash, pinContext);
                        addedContentSize = contentSize;
                        return new ContentFileInfo(Clock, contentSize);
                    }
                }

                contentExistsInCache = await onContentAlreadyInCache(contentHash, primaryPath, fileInfo);

                if (!contentExistsInCache)
                {
                    failed = true;
                    return null;
                }

                PinContentIfContext(contentHash, pinContext);

                addedContentSize = fileInfo.FileSize;
                return fileInfo;
            });

            if (failed)
            {
                return (Success: false, ContentAlreadyExistsInCache: contentExistsInCache);
            }

            if (addedContentSize > 0)
            {
                _tracer.AddPutBytes(addedContentSize);
            }

            if (_announcer != null && addedContentSize > 0 && announceAddOnSuccess)
            {
                await _announcer.ContentAdded(new ContentHashWithSize(contentHash, addedContentSize));
            }

            return (Success: true, ContentAlreadyExistsInCache: contentExistsInCache);
        }

        /// <inheritdoc />
        public Task<PutResult> PutStreamAsync(Context context, Stream stream, ContentHash contentHash, PinRequest? pinRequest)
        {
            return _tracer.PutStreamAsync(OperationContext(context), contentHash, async () =>
            {
                using (LockSet<ContentHash>.LockHandle contentHashHandle = await _lockSet.AcquireAsync(contentHash))
                {
                    CheckPinned(contentHash, pinRequest);
                    long contentSize = await GetContentSizeInternalAsync(context, contentHash, pinRequest?.PinContext);
                    if (contentSize >= 0)
                    {
                        // The user provided a hash for content that we already have. Try to satisfy the request without hashing the given stream.
                        (bool putInternalSucceeded, bool contentAlreadyExistsInCache) = await PutContentInternalAsync(
                            context,
                            contentHash,
                            contentSize,
                            pinRequest?.PinContext,
                            onContentAlreadyInCache: (hashHandle, primaryPath, info) => Task.FromResult(true),
                            onContentNotInCache: primaryPath => Task.FromResult(false));

                        if (putInternalSucceeded)
                        {
                            return new PutResult(contentHash, contentSize, contentAlreadyExistsInCache)
                                .WithLockAcquisitionDuration(contentHashHandle);
                        }
                    }
                }

                var r = await PutStreamImplAsync(context, stream, contentHash.HashType, pinRequest);

                return r.ContentHash != contentHash && r.Succeeded
                    ? new PutResult(r, contentHash, $"Calculated hash={r.ContentHash.ToShortString()} does not match caller's hash={contentHash.ToShortString()}")
                    : r;
            });
        }

        /// <inheritdoc />
        public Task<PutResult> PutStreamAsync(Context context, Stream stream, HashType hashType, PinRequest? pinRequest)
        {
            return _tracer.PutStreamAsync(
                OperationContext(context),
                hashType,
                () => PutStreamImplAsync(context, stream, hashType, pinRequest));
        }

        private async Task<PutResult> PutStreamImplAsync(Context context, Stream stream, HashType hashType, PinRequest? pinRequest)
        {
            ContentHash contentHash = new ContentHash(hashType);
            AbsolutePath? pathToTempContent = null;

            bool shouldDelete = false;
            try
            {
                long contentSize;

                var hasher = ContentHashers.Get(hashType);
                long length = stream.CanSeek ? stream.Length : -1;
                using (var hashingStream = hasher.CreateReadHashingStream(length, stream))
                {
                    pathToTempContent = await WriteToTemporaryFileAsync(context, hashingStream);
                    contentSize = FileSystem.GetFileSize(pathToTempContent);
                    contentHash = hashingStream.GetContentHash();

                    // This our temp file and it is responsibility of this method to delete it.
                    shouldDelete = true;
                }

                using (LockSet<ContentHash>.LockHandle contentHashHandle = await _lockSet.AcquireAsync(contentHash))
                {
                    CheckPinned(contentHash, pinRequest);

                    (bool putInternalSucceeded, bool contentAlreadyExistsInCache) = await PutContentInternalAsync(
                        context,
                        contentHash,
                        contentSize,
                        pinRequest?.PinContext,
                        onContentAlreadyInCache: (hashHandle, primaryPath, info) => Task.FromResult(true),
                        onContentNotInCache: async primaryPath =>
                        {
                            // ReSharper disable once AccessToModifiedClosure
                            await RetryOnUnexpectedReplicaAsync(
                                context,
                                () =>
                                {
                                    FileSystem.MoveFile(pathToTempContent, primaryPath, replaceExisting: false);
                                    return Task.FromResult(true);
                                },
                                contentHash,
                                expectedReplicaCount: 0);

                            pathToTempContent = null;
                            return true;
                        });
                    if (!putInternalSucceeded)
                    {
                        return new PutResult(contentHash, $"{nameof(PutStreamAsync)} failed to put {pathToTempContent} with hash {contentHash.ToShortString()} with an unknown error");
                    }

                    return new PutResult(contentHash, contentSize, contentAlreadyExistsInCache)
                        .WithLockAcquisitionDuration(contentHashHandle);
                }
            }
            finally
            {
                if (shouldDelete)
                {
                    DeleteTempFile(context, contentHash, pathToTempContent);
                }
            }
        }

        /// <summary>
        ///     Deletes a file that is marked read-only
        /// </summary>
        protected void DeleteReadOnlyFile(AbsolutePath path)
        {
            FileSystem.DeleteFile(path);
        }

        private void ForceDeleteFile(AbsolutePath path)
        {
            if (path == null)
            {
                return;
            }

            DeleteReadOnlyFile(path);
        }

        private void TryForceDeleteFile(Context context, AbsolutePath path)
        {
            try
            {
                ForceDeleteFile(path);
            }
            catch (Exception exception) when (exception is IOException || exception is BuildXLException || exception is UnauthorizedAccessException)
            {
                _tracer.Debug(context, $"Unable to force delete {path.Path} exception=[{exception}]");
            }
        }

        private void DeleteTempFile(Context context, ContentHash contentHash, AbsolutePath? path)
        {
            if (path == null)
            {
                return;
            }

            if (!path.GetParent().Equals(_tempFolder))
            {
                _tracer.Error(context, $"Will not delete temp file in unexpected location, path=[{path}]");
                return;
            }

            try
            {
                ForceDeleteFile(path);
                _tracer.Debug(context, $"Deleted temp content at '{path.Path}' for {contentHash.ToShortString()}");
            }
            catch (Exception exception) when (exception is IOException || exception is BuildXLException || exception is UnauthorizedAccessException)
            {
                _tracer.Warning(
                    context,
                    $"Unable to delete temp content at '{path.Path}' for {contentHash.ToShortString()} due to exception: {exception}");
            }
        }

        private AbsolutePath GetTemporaryFileName()
        {
            return _tempFolder / GetRandomFileName();
        }

        private AbsolutePath GetTemporaryFileName(ContentHash contentHash)
        {
            return _tempFolder / (GetRandomFileName() + contentHash.ToHex());
        }

        private static string GetRandomFileName()
        {
            // Don't use Path.GetRandomFileName(), it's not random enough when running multi-threaded.
            return Guid.NewGuid().ToString("N").Substring(0, 12);
        }

        /// <summary>
        ///     Writes the content stream to local disk in a temp directory under the store's root.
        ///     Marks the file as Read Only and sets ACL to deny file writes.
        /// </summary>
        private async Task<AbsolutePath> WriteToTemporaryFileAsync(Context context, Stream inputStream)
        {
            AbsolutePath pathToTempContent = GetTemporaryFileName();
            FileSystem.CreateDirectory(pathToTempContent.GetParent());

            // We want to set an ACL which denies writes before closing the destination stream. This way, there
            // are no instants in which we have neither an exclusive lock on writing the file nor a protective
            // ACL. Note that we can still be fooled in the event of external tampering via renames/move, but this
            // approach makes it very unlikely that our own code would ever write to or truncate the file before we move it.

            using (Stream tempFileStream = await FileSystem.OpenSafeAsync(pathToTempContent, FileAccess.Write, FileMode.CreateNew, FileShare.Delete))
            {
                await inputStream.CopyToWithFullBufferAsync(tempFileStream, FileSystemConstants.FileIOBufferSize);
                ApplyPermissions(context, pathToTempContent, FileAccessMode.ReadOnly);
            }

            return pathToTempContent;
        }

        private async Task<ContentHashWithSize> HashContentAsync(Context context, StreamWithLength stream, HashType hashType, AbsolutePath path)
        {
            try
            {
                ContentHash contentHash = await ContentHashers.Get(hashType).GetContentHashAsync(stream);
                return new ContentHashWithSize(contentHash, stream.Length);
            }
            catch (Exception e)
            {
                _tracer.Error(context, e, "Error while hashing content.");
                throw;
            }
        }

        private void ApplyPermissions(Context context, AbsolutePath path, FileAccessMode accessMode)
        {
            using var trace = _tracer.ApplyPerms();
            if (accessMode == FileAccessMode.ReadOnly)
            {
                FileSystem.DenyFileWrites(path);

                if (_applyDenyWriteAttributesOnContent)
                {
                    if (_applyDenyWriteAttributesOnContent && !IsNormalEnough(path))
                    {
                        // Only normalize attributes if DenyWriteAttributesOnContent is set
                        Normalize(path);
                    }

                    FileSystem.DenyAttributeWrites(path);

                    if (!IsNormalEnough(path))
                    {
                        throw new CacheException("The attributes of file {0} were modified during ingress. Found flags: {1}", path, File.GetAttributes(path.Path).ToString());
                    }
                }
            }
            else if (_applyDenyWriteAttributesOnContent && !IsNormalEnough(path))
            {
                // Only normalize attributes if DenyWriteAttributesOnContent is set
                Normalize(path);
            }

            // When DenyWriteAttributesOnContent is set to false, we shouldn't give an error
            // even if clearing potential Deny-WriteAttributes fails.  This is especially true
            // because in most cases where we're unable to clear those ACLs, we were probably
            // unable to set them in the first place.
            if (!_applyDenyWriteAttributesOnContent)
            {
                try
                {
                    FileSystem.AllowAttributeWrites(path);
                }
                catch (IOException ex)
                {
                    context.Warning("AllowAttributeWrites failed: " + ex);
                }
            }
        }

        private void Normalize(AbsolutePath path)
        {
            try
            {
                FileSystem.SetFileAttributes(path, FileAttributes.Normal);
            }
            catch (IOException)
            {
                FileSystem.AllowAttributeWrites(path);
                FileSystem.SetFileAttributes(path, FileAttributes.Normal);
            }
            catch (UnauthorizedAccessException)
            {
                FileSystem.AllowAttributeWrites(path);
                FileSystem.SetFileAttributes(path, FileAttributes.Normal);
            }
        }

        // Since setting ACLs seems to flip on the Archive bit,
        // we have to be content with allowing the archive bit to be set for cache blobs
        // We're allowlisting even more here because other values (that we don't care about)
        // sometimes survive being set to "Normal," and we don't want to throw in those cases.
        private bool IsNormalEnough(AbsolutePath path)
        {
            const FileAttributes IgnoredFileAttributes =
                FileAttributes.Normal | FileAttributes.Archive | FileAttributes.Compressed |
                FileAttributes.SparseFile | FileAttributes.Encrypted | FileAttributes.Offline |
                FileAttributes.IntegrityStream | FileAttributes.NoScrubData | FileAttributes.System |
                FileAttributes.Temporary | FileAttributes.Device | FileAttributes.Directory |
                FileAttributes.NotContentIndexed | FileAttributes.ReparsePoint | FileAttributes.Hidden;
            return FileSystem.FileAttributesAreSubset(path, IgnoredFileAttributes);
        }

        private enum Counter
        {
            [CounterType(CounterType.Stopwatch)]
            PutFileFast,
        }

        private enum ForEachReplicaCallbackResult
        {
            StopIterating,
            TryNextReplicaIfExists,
            TryNextReplica
        }

        private enum ReplicaExistence
        {
            Exists,
            DoesNotExist
        }

        private delegate Task<ForEachReplicaCallbackResult> ForEachReplicaCallback(
            AbsolutePath primaryPath, int replicaIndex, AbsolutePath replicaPath, bool replicaExists);

        // Perform the callback for each replica, starting from the primary replica (index 0)
        private async Task ForEachReplicaAsync(
            LockSet<ContentHash>.LockHandle contentHashHandle, ContentFileInfo info, ForEachReplicaCallback pathCallback)
        {
            AbsolutePath primaryPath = GetPrimaryPathFor(contentHashHandle.Key);
            ForEachReplicaCallbackResult result = await pathCallback(primaryPath, 0, primaryPath, true);
            if (result == ForEachReplicaCallbackResult.StopIterating)
            {
                return;
            }

            for (int replicaIndex = 1;
                replicaIndex < info.ReplicaCount || result == ForEachReplicaCallbackResult.TryNextReplica;
                replicaIndex++)
            {
                var replicaPath = GetReplicaPathFor(contentHashHandle.Key, replicaIndex);

                result = await pathCallback(primaryPath, replicaIndex, replicaPath, replicaIndex < info.ReplicaCount);
                if (result == ForEachReplicaCallbackResult.StopIterating)
                {
                    return;
                }
            }
        }

        /// <summary>
        ///     Gets the path that points to the location of a particular replica of this content hash.
        /// </summary>
        /// <param name="contentHash">Content hash to get path for</param>
        /// <param name="replicaIndex">The index of the replica. 0 is the primary.</param>
        /// <returns>Path for the hash</returns>
        /// <remarks>Does not guarantee anything is at the returned path</remarks>
        protected AbsolutePath GetReplicaPathFor(ContentHash contentHash, int replicaIndex)
        {
            Contract.Requires(replicaIndex >= 0);

            // MOve hashtype into inner call
            return _contentRootDirectory / contentHash.HashType.Serialize() / GetRelativePathFor(contentHash, replicaIndex);
        }

        /// <summary>
        ///     Gets the path that points to the location of this content hash.
        /// </summary>
        /// <param name="contentHash">Content hash to get path for</param>
        /// <returns>Path for the hash</returns>
        /// <remarks>Does not guarantee anything is at the returned path</remarks>
        public AbsolutePath GetPrimaryPathFor(ContentHash contentHash)
        {
            return GetReplicaPathFor(contentHash, 0);
        }

        /// <summary>
        /// Gets the relative path to the primary replica from the CAS root
        /// </summary>
        public static RelativePath GetPrimaryRelativePath(ContentHash contentHash)
        {
            return new RelativePath(Path.Combine(Constants.SharedDirectoryName, contentHash.HashType.Serialize(), GetRelativePathFor(contentHash, 0).Path));
        }

        private static RelativePath GetRelativePathFor(ContentHash contentHash, int replicaIndex)
        {
            string hash = contentHash.ToHex();

            // Create a subdirectory to not stress directory-wide locks used by the file system
            var hashSubDirectory = GetHashSubDirectory(contentHash);

            if (replicaIndex == 0)
            {
                return hashSubDirectory / string.Format(CultureInfo.InvariantCulture, "{0}.{1}", hash, BlobNameExtension);
            }

            return hashSubDirectory /
                   string.Format(CultureInfo.InvariantCulture, "{0}.{1}.{2}", hash, replicaIndex, BlobNameExtension);
        }

        private static RelativePath GetHashSubDirectory(ContentHash contentHash)
        {
            return new RelativePath(contentHash.ToHex().Substring(0, HashDirectoryNameLength));
        }

        internal bool TryGetFileInfo(ContentHash contentHash, out ContentFileInfo fileInfo) => ContentDirectory.TryGetFileInfo(contentHash, out fileInfo);

        internal ContentDirectorySnapshot<FileInfo> ReadSnapshotFromDisk(Context context)
        {
            // We are using a list of classes instead of structs due to the maximum object size restriction
            // When the contents on disk grow large, a list of structs surpasses the limit and forces OOM
            var contentHashes = new ContentDirectorySnapshot<FileInfo>();
            if (_settings.UseNativeBlobEnumeration)
            {
                EnumerateBlobPathsFromDisk(context, fileInfo => parseAndAccumulateContentHashes(fileInfo));
            }
            else
            {
                foreach (var fileInfo in EnumerateBlobPathsFromDisk())
                {
                    parseAndAccumulateContentHashes(fileInfo);
                }
            }

            return contentHashes;

            void parseAndAccumulateContentHashes(FileInfo fileInfo)
            {
                // A directory could have an old hash in its name or may be renamed by the user.
                // This is not an error condition if we can't get the hash out of it.
                if (TryGetHashFromPath(fileInfo.FullPath, out var contentHash))
                {
                    contentHashes.Add(new PayloadFromDisk<FileInfo>(contentHash, fileInfo));
                }
                else
                {
                    _tracer.Debug(context, $"Can't process directory '{fileInfo.FullPath}' because the path does not contain a well-known hash name.");
                }
            }
        }

        private IEnumerable<FileInfo> EnumerateBlobPathsFromDisk()
        {
            if (!FileSystem.DirectoryExists(_contentRootDirectory))
            {
                return new FileInfo[] {};
            }

            return FileSystem
                .EnumerateFiles(_contentRootDirectory, EnumerateOptions.Recurse)
                .Where(
                    fileInfo => fileInfo.FullPath.Path.EndsWith(BlobNameExtension, StringComparison.OrdinalIgnoreCase));
        }

        private void EnumerateBlobPathsFromDisk(Context context, Action<FileInfo> fileHandler)
        {
            try
            {
                FileSystem.EnumerateFiles(_contentRootDirectory, $"*.{BlobNameExtension}", recursive: true, fileHandler);
            }
            catch (IOException e)
            {
                _tracer.Info(context, $"Error enumerating blobs: {e}");
            }
        }

        private IEnumerable<FileInfo> EnumerateBlobPathsFromDiskFor(ContentHash contentHash)
        {
            var hashSubPath = _contentRootDirectory / contentHash.HashType.ToString() / GetHashSubDirectory(contentHash);
            if (!FileSystem.DirectoryExists(hashSubPath))
            {
                return new FileInfo[] {};
            }

            return FileSystem
                .EnumerateFiles(hashSubPath, EnumerateOptions.None)
                .Where(fileInfo =>
                {
                    var filePath = fileInfo.FullPath;
                    return TryGetHashFromPath(filePath, out var hash) &&
                           hash.Equals(contentHash) &&
                           filePath.FileName.EndsWith(BlobNameExtension, StringComparison.OrdinalIgnoreCase);
                });
        }

        internal static bool TryGetHashFromPath(AbsolutePath path, out ContentHash contentHash)
        {
            var hashName = path.GetParent().GetParent().FileName;
            if (Enum.TryParse<HashType>(hashName, ignoreCase: true, out var hashType))
            {
                string hashHexString = GetFileNameWithoutExtension(path);
                try
                {
                    contentHash = new ContentHash(hashType, HexUtilities.HexToBytes(hashHexString));
                }
                catch (ArgumentException)
                {
                    // If the file name format is malformed, throw an exception with more actionable error message.
                    throw new CacheException($"Failed to obtain the hash from file name '{path}'. File name should be in hexadecimal form.");
                }

                return true;
            }

            contentHash = default;
            return false;
        }

        /// <nodoc />
        public static string GetFileNameWithoutExtension(AbsolutePath path)
        {
            // Unlike <see cref = "Path.GetFileNameWithoutExtension" /> this method returns the name before the first '.', not the name until the last '.'.
            // I.e. for a file name <code>"foo.bar.baz"</code> this method returns "foo", but <see cref="Path.GetFileNameWithoutExtension"/> returns "foo.bar".

            Contract.Requires(path != null);
            string fileName = path.GetFileName();
            if (fileName.IndexOf('.') is var i && i == -1)
            {
                // No path extension found.
                return fileName;
            }
            return fileName.Substring(0, i);
        }

        private int GetReplicaIndexFromPath(AbsolutePath path)
        {
            if (TryGetHashFromPath(path, out var contentHash))
            {
                string fileName = path.GetFileName();

                // ReSharper disable once PossibleNullReferenceException
                if (fileName.StartsWith(contentHash.ToHex(), StringComparison.OrdinalIgnoreCase) &&
                    fileName.EndsWith(BlobNameExtension, StringComparison.OrdinalIgnoreCase))
                {
                    var fileNameParts = fileName.Split('.');
                    if (fileNameParts.Length == 2)
                    {
                        return 0;
                    }

                    if (fileNameParts.Length == 3)
                    {
                        if (int.TryParse(fileNameParts[1], out var index))
                        {
                            return index;
                        }
                    }
                }
            }

            return -1;
        }

        /// <summary>
        /// Snapshots the cached content in LRU order (i.e. the order, according to last-access time, in which they should be
        /// purged to make space).
        /// </summary>
        public virtual Task<IReadOnlyList<ContentHash>> GetLruOrderedContentListAsync()
        {
            return ContentDirectory.GetLruOrderedCacheContentAsync();
        }

        /// <summary>
        /// Snapshots the cached content in LRU order (i.e. the order, according to last-access time, in which they should be
        /// purged to make space). Coupled with its last-access time.
        /// </summary>
        public virtual Task<IReadOnlyList<ContentHashWithLastAccessTimeAndReplicaCount>> GetLruOrderedContentListWithTimeAsync()
        {
            return ContentDirectory.GetLruOrderedCacheContentWithTimeAsync();
        }

        private bool TryGetContentTotalSize(ContentHash contentHash, out long size)
        {
            if (ContentDirectory.TryGetFileInfo(contentHash, out var fileInfo))
            {
                size = fileInfo.TotalSize;
                return true;
            }

            size = 0;
            return false;
        }

        /// <summary>
        ///     Remove specified content.
        /// </summary>
        public Task<EvictResult> EvictAsync(Context context, ContentHashWithLastAccessTimeAndReplicaCount contentHashInfo, bool onlyUnlinked, Action<long>? evicted)
        {
            // This operation respects pinned content and won't evict it if it's pinned.
            return EvictCoreAsync(context, contentHashInfo, force: false, onlyUnlinked, evicted);
        }

        /// <inheritdoc />
        public async Task<DeleteResult> DeleteAsync(Context context, ContentHash contentHash, DeleteContentOptions? deleteOptions = null)
        {
            var evictResult = await EvictCoreAsync(context, new ContentHashWithLastAccessTimeAndReplicaCount(contentHash, DateTime.MinValue, safeToEvict: true), force: true, onlyUnlinked: false, (l) => { }, acquireLock: true);
            return evictResult.ToDeleteResult(contentHash);
        }

        private async Task<EvictResult> EvictCoreAsync(Context context, ContentHashWithLastAccessTimeAndReplicaCount contentHashInfo, bool force, bool onlyUnlinked, Action<long>? evicted, bool acquireLock = false)
        {
            ContentHash contentHash = contentHashInfo.ContentHash;

            long pinnedSize = 0;
            using (LockSet<ContentHash>.LockHandle? contentHashHandle = acquireLock ? await _lockSet.AcquireAsync(contentHash) : _lockSet.TryAcquire(contentHash))
            {
                if (contentHashHandle == null)
                {
                    _tracer.Debug(context, $"Skipping check of pinned size for {contentHash.ToShortString()} because another thread has a lock on it.");
                    return new EvictResult(contentHashInfo, evictedSize: 0, evictedFiles: 0, pinnedSize: 0, successfullyEvictedHash: false);
                }

                // Only checked PinMap if force is false, otherwise even pinned content should be evicted.
                if (!force && PinMap.TryGetValue(contentHash, out var pin) && pin.Count > 0)
                {
                    // The content is pinned. Eviction is not possible in this case.
                    if (TryGetContentTotalSize(contentHash, out var size))
                    {
                        pinnedSize = size;
                    }

                    return new EvictResult(contentHashInfo, evictedSize: 0, evictedFiles: 0, pinnedSize: pinnedSize, successfullyEvictedHash: false);
                }

                // Intentionally tracking only (potentially) successful eviction.
                return await _tracer.EvictAsync(
                    OperationContext(context),
                    contentHash,
                    async () =>
                    {
                        long evictedSize = 0;
                        long evictedFiles = 0;
                        bool successfullyEvictedHash = false;

                        await ContentDirectory.UpdateAsync(
                            contentHash,
                            touch: false,
                            Clock,
                            async fileInfo =>
                            {
                                if (fileInfo == null)
                                {
                                    // The content is not found in content directory.
                                    return null;
                                }

                                if (!force && PinMap.TryGetValue(contentHash, out pin) && pin.Count > 0)
                                {
                                    pinnedSize = fileInfo.TotalSize;

                                    // Nothing was modified, so no need to save anything.
                                    return null;
                                }

                                await ContentDirectory.RemoveAsync(contentHash);

                                var remainingReplicas = new List<AbsolutePath>(0);
                                var evictions = new List<ContentHashWithSize>();

                                // ReSharper disable once AccessToDisposedClosure
                                await ForEachReplicaAsync(
                                    contentHashHandle.Value,
                                    fileInfo,
                                    (primaryPath, replicaIndex, replicaPath, replicaExists) =>
                                    {
                                        bool exists = FileSystem.FileExists(replicaPath);
                                        bool evict = !exists || !onlyUnlinked || FileSystem.GetHardLinkCount(replicaPath) <= 1;
                                        if (evict)
                                        {
                                            try
                                            {
                                                if (exists)
                                                {
                                                    SafeForceDeleteFile(context, replicaPath);
                                                }

                                                evicted?.Invoke(fileInfo.FileSize);
                                                _tracer.Diagnostic(
                                                    context,
                                                    $"Evicted content hash=[{contentHash.ToShortString()}] replica=[{replicaIndex}] size=[{fileInfo.FileSize}]");
                                                evictedFiles++;
                                                evictedSize += fileInfo.FileSize;
                                                evictions.Add(new ContentHashWithSize(contentHash, fileInfo.FileSize));

                                                _tracer.TrackMetric(context, "ContentHashEvictedBytes", fileInfo.FileSize);
                                            }
                                            catch (Exception exception)
                                            {
                                                _tracer.Warning(
                                                    context,
                                                    $"Unable to purge {replicaPath.Path} because of exception: {exception}");
                                                remainingReplicas.Add(replicaPath);
                                            }
                                        }
                                        else
                                        {
                                            remainingReplicas.Add(replicaPath);
                                        }

                                        return Task.FromResult(ForEachReplicaCallbackResult.TryNextReplicaIfExists);
                                    });

                                if (_announcer != null)
                                {
                                    foreach (var e in evictions)
                                    {
                                        await _announcer.ContentEvicted(e);
                                    }
                                }

                                if (remainingReplicas.Count > 0)
                                {
                                    for (int i = 0; i < remainingReplicas.Count; i++)
                                    {
                                        AbsolutePath destinationPath = GetReplicaPathFor(contentHash, i);
                                        if (remainingReplicas[i] != destinationPath)
                                        {
                                            _tracer.Debug(
                                                context,
                                                $"Renaming [{remainingReplicas[i]}] to [{destinationPath}] as part of cleanup.");
                                            FileSystem.MoveFile(remainingReplicas[i], destinationPath, false);
                                        }
                                    }

                                    fileInfo.ReplicaCount = remainingReplicas.Count;
                                    return fileInfo;
                                }
                                else
                                {
                                    successfullyEvictedHash = true;
                                }

                                PinMap.TryRemove(contentHash, out pin);

                                return null;
                            });

                    return new EvictResult(contentHashInfo, evictedSize, evictedFiles, pinnedSize, successfullyEvictedHash);
                });
            }
        }

        /// <inheritdoc />
        public Task<PlaceFileResult> PlaceFileAsync(
            Context context,
            ContentHash contentHash,
            AbsolutePath destinationPath,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            PinRequest? pinRequest)
        {
            return _tracer.PlaceFileAsync(
                OperationContext(context),
                contentHash,
                destinationPath,
                accessMode,
                replacementMode,
                realizationMode,
                async () => await PlaceFileInternalAsync(
                    context,
                    new ContentHashWithPath(contentHash, destinationPath),
                    accessMode,
                    replacementMode,
                    realizationMode,
                    pinRequest));
        }

        /// <inheritdoc />
        public async Task<IEnumerable<Task<Indexed<PlaceFileResult>>>> PlaceFileAsync(
            Context context,
            IReadOnlyList<ContentHashWithPath> placeFileArgs,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            PinRequest? pinRequest = null)
        {
            var placeFileInternalBlock = new TransformBlock<Indexed<ContentHashWithPath>, Indexed<PlaceFileResult>>(
                async p =>
                    (await PlaceFileAsync(context, p.Item.Hash, p.Item.Path, accessMode, replacementMode, realizationMode, pinRequest)).WithIndex(p.Index),
                new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = ParallelPlaceFilesLimit, });

            // TODO: Better way ? (bug 1365340)
            placeFileInternalBlock.PostAll(placeFileArgs.AsIndexed());
            var results = await Task.WhenAll(Enumerable.Range(0, placeFileArgs.Count).Select(i => placeFileInternalBlock.ReceiveAsync()));
            placeFileInternalBlock.Complete();

            return results.AsTasks();
        }

        private async Task<PlaceFileResult> PlaceFileInternalAsync(
            Context context,
            ContentHashWithPath contentHashWithPath,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            PinRequest? pinRequest)
        {
            try
            {
                var contentHash = contentHashWithPath.Hash;
                var destinationPath = contentHashWithPath.Path;

                // Check for file existing in the non-racing SkipIfExists case.
                if ((replacementMode == FileReplacementMode.SkipIfExists || replacementMode == FileReplacementMode.FailIfExists) && FileSystem.FileExists(destinationPath))
                {
                    return new PlaceFileResult(PlaceFileResult.ResultCode.NotPlacedAlreadyExists);
                }

                // If this is the empty hash, then directly create an empty file.
                // This avoids hash-level lock, all I/O in the cache directory, and even
                // operations in the in-memory representation of the cache.
                if (contentHashWithPath.Hash.IsEmptyHash())
                {
                    await FileSystem.CreateEmptyFileAsync(contentHashWithPath.Path);
                    return new PlaceFileResult(PlaceFileResult.ResultCode.PlacedWithCopy);
                }

                // Lookup hash in content directory
                using (LockSet<ContentHash>.LockHandle contentHashHandle = await _lockSet.AcquireAsync(contentHash))
                {
                    if (pinRequest.HasValue)
                    {
                        PinContentIfContext(contentHash, pinRequest.Value.PinContext);
                    }

                    var code = PlaceFileResult.ResultCode.Unknown;
                    long contentSize = 0;
                    DateTime lastAccessTime = DateTime.MinValue;

                    await ContentDirectory.UpdateAsync(contentHash, true, Clock, async fileInfo =>
                    {
                        if (fileInfo == null)
                        {
                            code = PlaceFileResult.ResultCode.NotPlacedContentNotFound;
                            return null;
                        }

                        contentSize = fileInfo.FileSize;
                        lastAccessTime = DateTime.FromFileTimeUtc(fileInfo.LastAccessedFileTimeUtc);

                        if (ShouldAttemptHardLink(destinationPath, accessMode, realizationMode))
                        {
                            // ReSharper disable once AccessToDisposedClosure
                            CreateHardLinkResult hardLinkResult = await PlaceLinkFromCacheAsync(
                                    context,
                                    destinationPath,
                                    replacementMode,
                                    realizationMode,
                                    contentHash,
                                    fileInfo);
                            if (hardLinkResult == CreateHardLinkResult.Success)
                            {
                                code = PlaceFileResult.ResultCode.PlacedWithHardLink;
                                UnixHelpers.OverrideFileAccessMode(_settings.OverrideUnixFileAccessMode, destinationPath.Path);
                            }
                            else if (hardLinkResult == CreateHardLinkResult.FailedDestinationExists)
                            {
                                code = PlaceFileResult.ResultCode.NotPlacedAlreadyExists;
                            }
                            else if (hardLinkResult == CreateHardLinkResult.FailedSourceDoesNotExist)
                            {
                                await RemoveEntryIfNotOnDiskAsync(context, contentHash);
                                code = PlaceFileResult.ResultCode.NotPlacedContentNotFound;
                                return null;
                            }
                        }

                        return fileInfo;
                    });

                    if (code != PlaceFileResult.ResultCode.Unknown)
                    {
                        UnixHelpers.OverrideFileAccessMode(_settings.OverrideUnixFileAccessMode, destinationPath.Path);
                        return new PlaceFileResult(code, contentSize)
                            .WithLockAcquisitionDuration(contentHashHandle);
                    }

                    // If hard linking failed or wasn't attempted, fall back to copy.
                    var stopwatch = Stopwatch.StartNew();
                    try
                    {
                        PlaceFileResult result;
                        if (realizationMode == FileRealizationMode.CopyNoVerify)
                        {
                            result = await CopyFileWithNoValidationAsync(
                                context, contentHash, destinationPath, accessMode, replacementMode);
                        }
                        else
                        {
                            result = await CopyFileAndValidateStreamAsync(
                                context, contentHash, destinationPath, accessMode, replacementMode);
                        }

                        result.FileSize = contentSize;
                        result.LastAccessTime = lastAccessTime;
                        UnixHelpers.OverrideFileAccessMode(_settings.OverrideUnixFileAccessMode, destinationPath.Path);

                        return result
                            .WithLockAcquisitionDuration(contentHashHandle);
                    }
                    finally
                    {
                        stopwatch.Stop();
                        _tracer.PlaceFileCopy(context, destinationPath, contentHash, stopwatch.Elapsed);
                    }
                }
            }
            catch (Exception e)
            {
                return new PlaceFileResult(e);
            }
        }

        private async Task<PlaceFileResult> CopyFileWithNoValidationAsync(
            Context context,
            ContentHash contentHash,
            AbsolutePath destinationPath,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode)
        {
            var code = PlaceFileResult.ResultCode.PlacedWithCopy;
            AbsolutePath? contentPath = await PinContentAndGetFullPathAsync(contentHash, null);
            try
            {
                if (contentPath == null)
                {
                    code = PlaceFileResult.ResultCode.NotPlacedContentNotFound;
                }
                else
                {
                    try
                    {
                        var replaceExisting = replacementMode == FileReplacementMode.ReplaceExisting;
                        await FileSystem.CopyFileAsync(contentPath, destinationPath, replaceExisting);
                    }
                    catch (IOException e)
                    {
                        if (e.HResult == Hresult.FileExists || (e.InnerException != null &&
                                                                      e.InnerException.HResult == Hresult.FileExists))
                        {
                            // File existing in the racing SkipIfExists case.
                            code = PlaceFileResult.ResultCode.NotPlacedAlreadyExists;
                        }
                        else
                        {
                            return new PlaceFileResult(e, $"Failed to place hash=[{contentHash.ToShortString()}] to path=[{destinationPath}]");
                        }
                    }

                    ApplyPermissions(context, destinationPath, accessMode);
                }
            }
            finally
            {
                if (PinMap.TryGetValue(contentHash, out var pin))
                {
                    pin.Decrement();
                }
            }

            return new PlaceFileResult(code);
        }

        private async Task<PlaceFileResult> CopyFileAndValidateStreamAsync(
            Context context,
            ContentHash contentHash,
            AbsolutePath destinationPath,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode)
        {
            var code = PlaceFileResult.ResultCode.Unknown;
            ContentHash computedHash = new ContentHash(contentHash.HashType);
            var hasher = ContentHashers.Get(contentHash.HashType);

            using (StreamWithLength? contentStream =
                await OpenStreamInternalWithLockAsync(context, contentHash, pinRequest: null, FileShare.Read | FileShare.Delete))
            {
                if (contentStream == null)
                {
                    code = PlaceFileResult.ResultCode.NotPlacedContentNotFound;
                }
                else
                {
                    using (var hashingStream = hasher.CreateReadHashingStream(contentStream.Value))
                    {
                        try
                        {
                            FileSystem.CreateDirectory(destinationPath.GetParent());
                            var fileMode = replacementMode == FileReplacementMode.ReplaceExisting ? FileMode.Create : FileMode.CreateNew;

                            using (Stream targetFileStream = await FileSystem.OpenSafeAsync(destinationPath, FileAccess.Write, fileMode, FileShare.Delete))
                            {
                                await hashingStream.CopyToWithFullBufferAsync(
                                    targetFileStream, FileSystemConstants.FileIOBufferSize);

                                ApplyPermissions(context, destinationPath, accessMode);
                            }

                            computedHash = hashingStream.GetContentHash();
                        }
                        catch (IOException e)
                        {
                            if (e.InnerException != null && e.InnerException.HResult == Hresult.FileExists)
                            {
                                // File existing in the racing SkipIfExists case.
                                code = PlaceFileResult.ResultCode.NotPlacedAlreadyExists;
                            }
                            else
                            {
                                return new PlaceFileResult(e, $"Failed to place hash=[{contentHash.ToShortString()}] to path=[{destinationPath}]");
                            }
                        }
                    }
                }
            }

            if (code == PlaceFileResult.ResultCode.Unknown)
            {
                if (computedHash != contentHash)
                {
                    await RemoveCorruptedThenThrowAsync(context, contentHash, computedHash, destinationPath);
                }

                code = PlaceFileResult.ResultCode.PlacedWithCopy;
            }

            return new PlaceFileResult(code);
        }

        private async Task<CreateHardLinkResult> PlaceLinkFromCacheAsync(
            Context context,
            AbsolutePath destinationPath,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            ContentHash contentHash,
            ContentFileInfo info,
            bool fastPath = false)
        {
            FileSystem.CreateDirectory(destinationPath.GetParent());

            int defaultStartIndex = info.ReplicaCount - 1;

            // If a cursor has been saved for this hash, try that one first
            if (_replicaCursors.TryGetValue(contentHash, out var startIndex))
            {
                if (startIndex >= info.ReplicaCount || startIndex < 0)
                {
                    if (!fastPath)
                    {
                        // Don't remove because we cannot assume replica cursor in map has not been modified to valid value in fast path due to lack of locking
                        // Remove an out-of-range cursor
                        _replicaCursors.TryRemove(contentHash, out startIndex);
                    }

                    startIndex = defaultStartIndex;
                }
            }
            else
            {
                // If not, try the most recently created replica first
                startIndex = defaultStartIndex;
            }

            CreateHardLinkResult result = await PlaceLinkFromReplicaAsync(
                context,
                destinationPath,
                replacementMode,
                realizationMode,
                contentHash,
                info,
                startIndex,
                ReplicaExistence.Exists,
                fastPath);

            if (result != CreateHardLinkResult.FailedMaxHardLinkLimitReached)
            {
                return result;
            }

            if (!fastPath)
            {
                // Don't remove because we cannot assume replica cursor in map has not been modified to valid value in fast path due to lack of locking
                // This replica is full
                _replicaCursors.TryRemove(contentHash, out _);
            }

            if (info.ReplicaCount > 1)
            {
                // Try a random existing replica before making a new one.
                var randomIndex = ThreadSafeRandom.Generator.Next(info.ReplicaCount - 1);
                result = await PlaceLinkFromReplicaAsync(
                    context,
                    destinationPath,
                    replacementMode,
                    realizationMode,
                    contentHash,
                    info,
                    randomIndex,
                    ReplicaExistence.Exists,
                    fastPath);
                if (result != CreateHardLinkResult.FailedMaxHardLinkLimitReached)
                {
                    // Save the cursor here as the most recent replica tried. No contention on the value due to the lock.
                    _replicaCursors.AddOrUpdate(contentHash, randomIndex, (hash, i) => randomIndex);
                    _tracer.Debug(
                        context,
                        $"Moving replica cursor to index {randomIndex} because callback stopped on replica {GetReplicaPathFor(contentHash, randomIndex).Path}.");
                    return result;
                }
            }

            if (fastPath)
            {
                // In the fast path, we don't want to attempt to create a replica, just reuse existing replicas
                // Assume(result == CreateHardLinkResult.FailedMaxHardLinkLimitReached)
                return result;
            }

            var newReplicaIndex = info.ReplicaCount;
            return await PlaceLinkFromReplicaAsync(
                context,
                destinationPath,
                replacementMode,
                realizationMode,
                contentHash,
                info,
                newReplicaIndex,
                ReplicaExistence.DoesNotExist,
                fastPath: false);
        }

        private async Task<CreateHardLinkResult> PlaceLinkFromReplicaAsync(
            Context context,
            AbsolutePath destinationPath,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            ContentHash contentHash,
            ContentFileInfo info,
            int replicaIndex,
            ReplicaExistence replicaExistence,
            bool fastPath)
        {
            Contract.Assert(QuotaKeeper != null);
            Contract.Assert(!(replicaExistence != ReplicaExistence.Exists && fastPath), "PlaceLinkFromReplicaAsync should only be called for with fastPath=true for existing replicas");

            var primaryPath = GetPrimaryPathFor(contentHash);
            var replicaPath = GetReplicaPathFor(contentHash, replicaIndex);
            if (replicaExistence == ReplicaExistence.DoesNotExist)
            {
                // Create a new replica
                using (var txn = await QuotaKeeper.ReserveAsync(info.FileSize))
                {
                    await RetryOnUnexpectedReplicaAsync(
                        context,
                        () => SafeCopyFileAsync(
                                    context,
                                    contentHash,
                                    primaryPath,
                                    replicaPath,
                                    FileReplacementMode.FailIfExists),
                    contentHash,
                    info.ReplicaCount);
                    txn.Commit();
                }

                if (_announcer != null)
                {
                    await _announcer.ContentAdded(new ContentHashWithSize(contentHash, info.FileSize));
                }

                info.ReplicaCount++;
            }

            if (!TryCreateHardlink(
                context,
                replicaPath,
                destinationPath,
                realizationMode,
                replacementMode == FileReplacementMode.ReplaceExisting,
                out CreateHardLinkResult hardLinkResult))
            {
                // Don't attempt to create the replica in the fast path (not locking so don't modify in CAS disk state)
                if (!fastPath &&
                    hardLinkResult == CreateHardLinkResult.FailedSourceDoesNotExist &&
                    primaryPath != replicaPath &&
                    FileSystem.FileExists(primaryPath))
                {
                    _tracer.Warning(
                        context,
                        $"Missing replica for hash=[{contentHash.ToShortString()}]. Recreating replica=[{replicaPath}] from primary replica.");
                    Interlocked.Increment(ref _contentDirectoryMismatchCount);
                    await SafeCopyFileAsync(
                        context,
                        contentHash,
                        primaryPath,
                        replicaPath,
                        FileReplacementMode.FailIfExists);
                    TryCreateHardlink(
                        context,
                        replicaPath,
                        destinationPath,
                        realizationMode,
                        replacementMode == FileReplacementMode.ReplaceExisting,
                        out hardLinkResult);
                }
            }

            return hardLinkResult;
        }

        private async Task SafeCopyFileAsync(
            Context context,
            ContentHash contentHash,
            AbsolutePath sourcePath,
            AbsolutePath destinationPath,
            FileReplacementMode replacementMode)
        {
            AbsolutePath tempPath = GetTemporaryFileName(contentHash);
            await FileSystem.CopyFileAsync(sourcePath, tempPath, false);
            ApplyPermissions(context, tempPath, FileAccessMode.ReadOnly);
            FileSystem.MoveFile(tempPath, destinationPath, replacementMode == FileReplacementMode.ReplaceExisting);
        }

        private async Task RetryOnUnexpectedReplicaAsync(
            Context context, Func<Task> tryFunc, ContentHash contentHash, int expectedReplicaCount)
        {
            try
            {
                await tryFunc();
            }
            catch (IOException)
            {
                RemoveExtraReplicasFromDiskFor(context, contentHash, expectedReplicaCount);
                await tryFunc();
            }
        }

        /// <summary>
        ///     Removes the corresponding entry from the content directory if the content for a hash doesn't exist on disk.
        /// </summary>
        /// <param name="context">Tracing context</param>
        /// <param name="contentHash">The hash whose content and content directory entry are in question.</param>
        /// <returns>Whether a bad entry was removed.</returns>
        private async Task<bool> RemoveEntryIfNotOnDiskAsync(Context context, ContentHash contentHash)
        {
            var primaryPath = GetPrimaryPathFor(contentHash);
            if (!FileSystem.FileExists(primaryPath) && (await ContentDirectory.RemoveAsync(contentHash) != null))
            {
                _tracer.Warning(
                    context,
                    $"Removed content directory entry for hash {contentHash.ToShortString()} because the cache does not have content at {primaryPath}.");
                Interlocked.Increment(ref _contentDirectoryMismatchCount);
                return true;
            }

            return false;
        }

        private void RemoveAllReplicasFromDiskFor(Context context, ContentHash contentHash)
        {
            RemoveExtraReplicasFromDiskFor(context, contentHash, 0);
        }

        /// <summary>
        ///     Removes all replicas for the given hash beyond the expected number from disk.
        /// </summary>
        /// <param name="context">Tracing context</param>
        /// <param name="contentHash">The hash whose replicas are to be limited.</param>
        /// <param name="expectedReplicaCount">The number of replicas to which the hash's replicas are to be limited.</param>
        private void RemoveExtraReplicasFromDiskFor(Context context, ContentHash contentHash, int expectedReplicaCount)
        {
            AbsolutePath[] extraReplicaPaths =
                EnumerateBlobPathsFromDiskFor(contentHash)
                    .Select(blobPath => blobPath.FullPath)
                    .Where(replicaPath => GetReplicaIndexFromPath(replicaPath) >= expectedReplicaCount).ToArray();

            if (extraReplicaPaths.Any())
            {
                _tracer.Warning(context, $"Unexpected cache blob for hash=[{contentHash.ToShortString()}]. Removing extra blob(s).");
                Interlocked.Increment(ref _contentDirectoryMismatchCount);
            }

            foreach (AbsolutePath extraReplicaPath in extraReplicaPaths)
            {
                _tracer.Debug(context, $"Deleting extra blob {extraReplicaPath.Path}.");
                SafeForceDeleteFile(context, extraReplicaPath);
            }
        }

        private async Task RemoveCorruptedThenThrowAsync(Context context, ContentHash contentHash, ContentHash computedHash, AbsolutePath destinationPath)
        {
            AbsolutePath[] replicaPaths = EnumerateBlobPathsFromDiskFor(contentHash).Select(blobPath => blobPath.FullPath).ToArray();

            foreach (AbsolutePath replicaPath in replicaPaths)
            {
                _tracer.Debug(context, $"Deleting corrupted blob {replicaPath.Path}.");
                SafeForceDeleteFile(context, replicaPath);
            }

            _tracer.Debug(context, $"Removing content directory entry for corrupted hash {contentHash.ToShortString()}.");
            await ContentDirectory.RemoveAsync(contentHash);

            throw new ContentHashMismatchException(destinationPath, computedHash, contentHash);
        }

        /// <summary>
        ///     Delete the file or, if unable, move it to a temp location and force delete it.
        /// </summary>
        /// <remarks>
        ///     Use this to safely delete blobs from their canonical locations without leaving unprotected files around.
        /// </remarks>
        private void SafeForceDeleteFile(Context context, AbsolutePath path)
        {
            try
            {
                DeleteReadOnlyFile(path);
            }
            catch (Exception exception) when (exception is IOException || exception is BuildXLException || exception is UnauthorizedAccessException)
            {
                AbsolutePath tempPath = GetTemporaryFileName();
                _tracer.Debug(
                    context,
                    $"Unable to delete {path.Path} exception=[{exception}]. Moving to temp path=[{tempPath}] instead and attempting to delete more thoroughly.");
                FileSystem.MoveFile(path, tempPath, replaceExisting: false);
                TryForceDeleteFile(context, tempPath);
            }
        }

        private async Task PinContextDisposeAsync(IEnumerable<KeyValuePair<ContentHash, int>> pinCounts)
        {
            long pinnedSize = 0;

            foreach (var pinCount in pinCounts)
            {
                using (await _lockSet.AcquireAsync(pinCount.Key))
                {
                    if (PinMap.TryGetValue(pinCount.Key, out Pin? pin))
                    {
                        pin.Add(-1 * pinCount.Value);
                        if (pin.Count == 0)
                        {
                            PinMap.TryRemoveSpecific(pinCount.Key, pin);
                        }
                    }

                    if (TryGetContentTotalSize(pinCount.Key, out var size))
                    {
                        pinnedSize += size;
                    }
                }
            }

            Contract.Assert(QuotaKeeper != null);
            QuotaKeeper.Calibrate();

            Contract.Assert(_pinSizeHistory != null);
            lock (_pinSizeHistory)
            {
                _maxPinSize = Math.Max(pinnedSize, _maxPinSize);

                if (Interlocked.Decrement(ref _pinContextCount) == 0)
                {
                    _pinSizeHistory.Add(_maxPinSize);
                    _maxPinSize = -1;
                }
            }
        }

        /// <summary>
        ///     Increment the info's pin count and add the hash to the given context.
        /// </summary>
        private void PinContentIfContext(ContentHash hash, PinContext? pinContext)
        {
            if (pinContext != null)
            {
                IncrementPin(hash);

                pinContext.AddPin(hash);
            }
        }

        private void IncrementPin(ContentHash hash)
        {
            PinMap.GetOrAdd(hash, new Pin()).Increment();
        }

        /// <summary>
        /// Provides a PinContext for this cache which can be used in conjunction with other APIs to pin relevant content in
        /// the cache.
        /// The content may be unpinned by disposing of the PinContext.
        /// </summary>
        public PinContext CreatePinContext()
        {
            Interlocked.Increment(ref _pinContextCount);

            // ReSharper disable once RedundantArgumentName
            return new PinContext(_taskTracker, unpinAsync: pairs => PinContextDisposeAsync(pairs));
        }

        /// <summary>
        ///     Pin existing content.
        /// </summary>
        public Task<PinResult> PinAsync(Context context, ContentHash contentHash, PinContext? pinContext)
        {
            return _tracer.PinAsync(OperationContext(context), contentHash, async () =>
            {
                var bulkResults = await PinAsync(context, new[] { contentHash }, pinContext, PinBulkOptions.Default);
                return bulkResults.Single().Item;
            });
        }

        /// <inheritdoc />
        public async Task<IEnumerable<Indexed<PinResult>>> PinAsync(Context context, IReadOnlyList<ContentHash> contentHashes, PinContext? pinContext, PinBulkOptions? options)
        {
            var stopwatch = Stopwatch.StartNew();

            options ??= PinBulkOptions.Default;
            _tracer.PinBulkStart(context, contentHashes);

            var (results, error) = await PinCoreAsync(context, contentHashes, pinContext, options);

            _tracer.PinBulkStop(context, stopwatch.Elapsed, contentHashes: contentHashes, results: results, error, options);

            return results;
        }

        private async Task<(IEnumerable<Indexed<PinResult>> results, Exception? error)> PinCoreAsync(
            Context context,
            IReadOnlyList<ContentHash> contentHashes,
            PinContext? pinContext,
            PinBulkOptions options)
        {
            bool skipLockAndTouch = _settings.SkipTouchAndLockAcquisitionWhenPinningFromHibernation && options.RePinFromHibernation;

            var results = new List<PinResult>(contentHashes.Count);
            try
            {
                var pinRequest = new PinRequest(pinContext);

                // TODO: This is still relatively inefficient. We're taking a lock per hash and pinning each individually. (bug 1365340)
                // The batching needs to go further down.
                foreach (var contentHash in contentHashes)
                {
                    // Pinning the empty file always succeeds; no I/O or other operations required,
                    // because we have dedicated logic to place it when required.
                    if (contentHash.IsEmptyHash())
                    {
                        results.Add(new PinResult(contentSize: 0, lastAccessTime: Clock.UtcNow, code: PinResult.ResultCode.Success));
                    }
                    else
                    {
                        // Hot path optimization: instead of acquiring locks and touching the file system,
                        // we re-pin the content if it exits in memory content directory that was reconstructed (or reloaded) at startup
                        ContentFileInfo? contentInfo = null;
                        if (skipLockAndTouch)
                        {
                            // The following logic is not 100% thread safe, but during re-pinning process no other operations should be happening
                            // against the file system (like puts, places or eviction).
                            if (ContentDirectory.TryGetFileInfo(contentHash, out contentInfo))
                            {
                                PinContentIfContext(contentHash, pinContext);
                            }
                        }
                        else
                        {
                            contentInfo = await GetContentSizeAndLastAccessTimeAsync(context, contentHash, pinRequest);
                        }

                        results.Add(contentInfo != null ? new PinResult(contentInfo.FileSize, DateTime.FromFileTimeUtc(contentInfo.LastAccessedFileTimeUtc)) : PinResult.ContentNotFound);
                    }
                }

                return (results: results.AsIndexed(), error: null);
            }
            catch (Exception exception)
            {
                return (results: contentHashes.Select(x => PinResult.ContentNotFound).AsIndexed(), error: exception);
            }
        }

        /// <inheritdoc />
        public async Task<bool> ContainsAsync(Context context, ContentHash contentHash, PinRequest? pinRequest)
        {
            PinContext? pinContext = pinRequest?.PinContext;

            using (await _lockSet.AcquireAsync(contentHash))
            {
                bool found = false;
                await ContentDirectory.UpdateAsync(contentHash, touch: true, clock: Clock, updateFileInfo: async fileInfo =>
                {
                    // If _checkFiles is true, make an additional check whether the file is actually on the disk.
                    // Otherwise, we will just trust our in-memory record.
                    if (fileInfo != null && !(_settings.CheckFiles && await RemoveEntryIfNotOnDiskAsync(context, contentHash)))
                    {
                        found = true;
                        PinContentIfContext(contentHash, pinContext);
                    }

                    return null;
                });

                return found;
            }
        }

        private void CheckPinned(ContentHash contentHash, PinRequest? pinRequest)
        {
            if (pinRequest?.VerifyAlreadyPinned == true)
            {
                IsPinned(contentHash, pinRequest);
            }
        }

        /// <summary>
        /// Checks if whether content is locally pinned.
        /// </summary>
        public bool IsPinned(ContentHash contentHash, PinRequest? pinRequest = null)
        {
            var verifyAlreadyPinned = false;
            PinContext? verifyPinContext = null;

            if (pinRequest.HasValue)
            {
                verifyAlreadyPinned = pinRequest.Value.VerifyAlreadyPinned;
                verifyPinContext = pinRequest.Value.VerifyPinContext;
            }

            bool pinned = PinMap.TryGetValue(contentHash, out var pin) && pin.Count > 0;
            if (verifyAlreadyPinned)
            {
                if (!pinned)
                {
                    throw new CacheException("Expected content with hash {0} to be pinned, but it was not.", contentHash.ToShortString());
                }

                if (verifyPinContext != null && !verifyPinContext.Contains(contentHash))
                {
                    throw new CacheException(
                        "Expected content with hash {0} was pinned, but not to the expected pin context.", contentHash.ToShortString());
                }
            }

            return pinned;
        }

        /// <inheritdoc />
        public async Task<GetContentSizeResult> GetContentSizeAndCheckPinnedAsync(Context context, ContentHash contentHash, PinRequest? pinRequest)
        {
            using (await _lockSet.AcquireAsync(contentHash))
            {
                var contentWasPinned = IsPinned(contentHash, pinRequest);
                long contentSize = await GetContentSizeInternalAsync(context, contentHash, pinRequest?.PinContext);
                return new GetContentSizeResult(contentSize, contentWasPinned);
            }
        }

        private async Task<ContentFileInfo?> GetContentSizeAndLastAccessTimeAsync(Context context, ContentHash contentHash, PinRequest? pinRequest)
        {
            using (await _lockSet.AcquireAsync(contentHash))
            {
                return await GetContentSizeAndLastAccessTimeInternalAsync(context, contentHash, pinRequest?.PinContext);
            }
        }
        
        private async Task<long> GetContentSizeInternalAsync(Context context, ContentHash contentHash, PinContext? pinContext = null)
        {
            var info = await GetContentSizeAndLastAccessTimeInternalAsync(context, contentHash, pinContext);
            return info?.FileSize ?? -1;
        }

        private async Task<ContentFileInfo?> GetContentSizeAndLastAccessTimeInternalAsync(Context context, ContentHash contentHash, PinContext? pinContext = null)
        {
            ContentFileInfo? info = null;

            await ContentDirectory.UpdateAsync(contentHash, touch: true, clock: Clock, updateFileInfo: async contentFileInfo =>
            {
                if (contentFileInfo != null && !await RemoveEntryIfNotOnDiskAsync(context, contentHash))
                {
                    info = contentFileInfo;
                    PinContentIfContext(contentHash, pinContext);
                }

                return null;
            });

            return info;
        }

        /// <inheritdoc />
        public Task<OpenStreamResult> OpenStreamAsync(Context context, ContentHash contentHash, PinRequest? pinRequest)
        {
            return _tracer.OpenStreamAsync(OperationContext(context), contentHash, async () =>
            {
                // Short-circuit requests for the empty stream
                // No lock is required since no file is involved.
                if (contentHash.IsEmptyHash())
                {
                    return new OpenStreamResult(_emptyFileStream);
                }

                using (var lockHandle = await _lockSet.AcquireAsync(contentHash))
                {
                    var stream = await OpenStreamInternalWithLockAsync(context, contentHash, pinRequest, FileShare.Read | FileShare.Delete);
                    return new OpenStreamResult(stream)
                        .WithLockAcquisitionDuration(lockHandle);
                }
            });
        }

        private async Task<StreamWithLength?> OpenStreamInternalWithLockAsync(Context context, ContentHash contentHash, PinRequest? pinRequest, FileShare share)
        {
            AbsolutePath? contentPath = await PinContentAndGetFullPathAsync(contentHash, pinRequest);

            if (contentPath == null)
            {
                return null;
            }

            var contentStream = await FileSystem.OpenAsync(contentPath, FileAccess.Read, FileMode.Open, share);

            if (contentStream == null)
            {
                await RemoveEntryIfNotOnDiskAsync(context, contentHash);
                return null;
            }

            return contentStream;
        }

        private async Task<AbsolutePath?> PinContentAndGetFullPathAsync(ContentHash contentHash, PinRequest? pinRequest)
        {
            CheckPinned(contentHash, pinRequest);

            var found = false;
            await ContentDirectory.UpdateAsync(contentHash, true, Clock, fileInfo =>
            {
                if (fileInfo != null)
                {
                    found = true;
                    PinContentIfContext(contentHash, pinRequest?.PinContext);
                }

                return null;
            });

            if (!found)
            {
                return null;
            }

            return GetPrimaryPathFor(contentHash);
        }

        /// <summary>
        /// Gets whether the store contains the given content
        /// </summary>
        public bool Contains(ContentHash hash)
        {
            return ContentDirectory.TryGetFileInfo(hash, out _);
        }

        /// <summary>
        ///     Gives the maximum path to files stored under the cache root.
        /// </summary>
        public static int GetMaxContentPathLengthRelativeToCacheRoot()
        {
            var maxHashNameLength = HashInfoLookup.All().Max(v => v.Name.Length);
            var maxHashStringLength = HashInfoLookup.All().Max(v => v.StringLength);

            int maxContentPathLengthRelativeToCacheRoot =
                Constants.SharedDirectoryName.Length +
                1 + // path separator
                maxHashNameLength +
                1 + // path separator
                HashDirectoryNameLength + // hash directory
                1 + // path separator
                maxHashStringLength + // filename base, 2 characters per byte in hex string
                1 + // dot preceding filename extension
                BlobNameExtension.Length; // filename extension

            return maxContentPathLengthRelativeToCacheRoot;
        }

        /// <inheritdoc />
        public Task<PutResult> PutFileAsync(Context context, AbsolutePath path, HashType hashType, FileRealizationMode realizationMode, Func<Stream, Stream> wrapStream, PinRequest? pinRequest)
        {
            return PutFileImplAsync(context, path, realizationMode, hashType, pinRequest, trustedHashWithSize: null, wrapStream);
        }

        /// <inheritdoc />
        public Task<PutResult> PutFileAsync(Context context, AbsolutePath path, ContentHash contentHash, FileRealizationMode realizationMode, Func<Stream, Stream> wrapStream, PinRequest? pinRequest)
        {
            return PutFileImplAsync(context, path, realizationMode, contentHash, pinRequest, wrapStream);
        }

        private class NonClosingEmptyMemoryStream : MemoryStream
        {
            public NonClosingEmptyMemoryStream()
                : base(CollectionUtilities.EmptyArray<byte>(), writable: false)
            {
            }

            protected override void Dispose(bool disposing)
            {
                // Intentionally doing nothing to avoid closing the stream.
            }
        }
    }
}
