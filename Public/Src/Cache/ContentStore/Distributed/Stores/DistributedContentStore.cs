// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Sessions;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Cache.ContentStore.Distributed.Stores
{
    /// <summary>
    /// A store that is based on content locations for opaque file locations.
    /// </summary>
    /// <typeparam name="T">The content locations being stored.</typeparam>
    public class DistributedContentStore<T> : StartupShutdownBase, IContentStore, IRepairStore, IDistributedLocationStore, IStreamStore, ICopyRequestHandler, IPushFileHandler, IDeleteFileHandler, IDistributedContentCopierHost
        where T : PathBase
    {
        // Used for testing.
        internal enum Counters
        {
            ProactiveReplication_Succeeded, 
            ProactiveReplication_Failed,
            ProactiveReplication_Skipped,
            ProactiveReplication_Rejected,
            RejectedPushCopyCount_OlderThanEvicted,
            ProactiveReplication
        }

        internal readonly CounterCollection<Counters> CounterCollection = new CounterCollection<Counters>();

        /// <summary>
        /// The location of the local cache root
        /// </summary>
        public MachineLocation LocalMachineLocation { get; }

        private readonly IContentLocationStoreFactory _contentLocationStoreFactory;
        private readonly ContentStoreTracer _tracer = new ContentStoreTracer(nameof(DistributedContentStore<T>));
        private readonly IClock _clock;

        private DateTime? _lastEvictedEffectiveLastAccessTime;

        /// <summary>
        /// Flag for testing using local Redis instance.
        /// </summary>
        internal bool DisposeContentStoreFactory = true;

        internal IContentStore InnerContentStore { get; }

        /// <inheritdoc />
        protected override Tracer Tracer => _tracer;

        private IContentLocationStore _contentLocationStore;

        private readonly DistributedContentStoreSettings _settings;

        /// <summary>
        /// Task source that is set to completion state when the system is fully initialized.
        /// The main goal of this field is to avoid the race condition when eviction is triggered during startup
        /// when hibernated sessions are not fully reloaded.
        /// </summary>
        private readonly TaskSourceSlim<BoolResult> _postInitializationCompletion = TaskSourceSlim.Create<BoolResult>();

        private readonly DistributedContentCopier<T> _distributedCopier;
        private readonly DisposableDirectory _copierWorkingDirectory;
        internal Lazy<Task<Result<ReadOnlyDistributedContentSession<T>>>> ProactiveCopySession;

        /// <nodoc />
        public DistributedContentStore(
            MachineLocation localMachineLocation,
            AbsolutePath localCacheRoot,
            Func<ContentStoreSettings, IDistributedLocationStore, IContentStore> innerContentStoreFunc,
            IContentLocationStoreFactory contentLocationStoreFactory,
            DistributedContentStoreSettings settings,
            DistributedContentCopier<T> distributedCopier,
            IClock clock = null,
            ContentStoreSettings contentStoreSettings = null)
        {
            Contract.Requires(settings != null);

            LocalMachineLocation = localMachineLocation;
            _contentLocationStoreFactory = contentLocationStoreFactory;
            _clock = clock;
            _distributedCopier = distributedCopier;
            _copierWorkingDirectory = new DisposableDirectory(distributedCopier.FileSystem, localCacheRoot / "Temp");

            contentStoreSettings ??= ContentStoreSettings.DefaultSettings;
            _settings = settings;

            InnerContentStore = innerContentStoreFunc(contentStoreSettings, this);
        }

        AbsolutePath IDistributedContentCopierHost.WorkingFolder => _copierWorkingDirectory.Path;

        void IDistributedContentCopierHost.ReportReputation(MachineLocation location, MachineReputation reputation)
        {
            _contentLocationStore.MachineReputationTracker.ReportReputation(location, reputation);
        }

        private Task<Result<ReadOnlyDistributedContentSession<T>>> CreateCopySession(Context context)
        {
            var sessionId = Guid.NewGuid();

            var operationContext = OperationContext(context.CreateNested(sessionId, nameof(DistributedContentStore<T>)));
            return operationContext.PerformOperationAsync(_tracer,
                async () =>
                {
                    // NOTE: We use ImplicitPin.None so that the OpenStream calls triggered by RequestCopy will only pull the content, NOT pin it in the local store.
                    var sessionResult = CreateReadOnlySession(operationContext, $"{sessionId}-DefaultCopy", ImplicitPin.None).ThrowIfFailure();
                    var session = sessionResult.Session;

                    await session.StartupAsync(context).ThrowIfFailure();
                    return Result.Success(session as ReadOnlyDistributedContentSession<T>);
                });
        }

        /// <inheritdoc />
        public override Task<BoolResult> StartupAsync(Context context)
        {
            var startupTask = base.StartupAsync(context);

            ProactiveCopySession = new Lazy<Task<Result<ReadOnlyDistributedContentSession<T>>>>(() => CreateCopySession(context));

            if (_settings.SetPostInitializationCompletionAfterStartup)
            {
                context.Debug("Linking post-initialization completion task with the result of StartupAsync.");
                _postInitializationCompletion.LinkToTask(startupTask);
            }

            return startupTask;
        }

        /// <inheritdoc />
        public void PostInitializationCompleted(Context context, BoolResult result)
        {
            context.Debug($"Setting result for post-initialization completion task to '{result}'.");
            _postInitializationCompletion.TrySetResult(result);
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            // NOTE: We create and start the content location store before the inner content store just in case the
            // inner content store starts background eviction after startup. We need the content store to be initialized
            // so that it can be queried and used to unregister content.
            await _contentLocationStoreFactory.StartupAsync(context).ThrowIfFailure();

            _contentLocationStore = await _contentLocationStoreFactory.CreateAsync(LocalMachineLocation, InnerContentStore as ILocalContentStore);

            // Initializing inner store before initializing LocalLocationStore because
            // LocalLocationStore may use inner store for reconciliation purposes
            await InnerContentStore.StartupAsync(context).ThrowIfFailure();

            await _contentLocationStore.StartupAsync(context).ThrowIfFailure();

            if (_settings.EnableProactiveReplication
                && _contentLocationStore is TransitioningContentLocationStore tcs
                && InnerContentStore is ILocalContentStore localContentStore)
            {
                await ProactiveReplicationAsync(context.CreateNested(nameof(DistributedContentStore<T>)), localContentStore, tcs).ThrowIfFailure();
            }

            return BoolResult.Success;
        }

        private Task<BoolResult> ProactiveReplicationAsync(
            OperationContext context,
            ILocalContentStore localContentStore,
            TransitioningContentLocationStore contentLocationStore)
        {
            return context.PerformOperationAsync(
                   Tracer,
                   async () =>
                   {
                       var proactiveCopySession = await ProactiveCopySession.Value.ThrowIfFailureAsync();

                       await contentLocationStore.LocalLocationStore.EnsureInitializedAsync().ThrowIfFailure();

                       while (!context.Token.IsCancellationRequested)
                       {
                           // Create task before starting operation to ensure uniform intervals assuming operation takes less than the delay.
                           var delayTask = Task.Delay(_settings.ProactiveReplicationInterval, context.Token);

                           await ProactiveReplicationIterationAsync(context, proactiveCopySession, localContentStore, contentLocationStore).ThrowIfFailure();

                           if (_settings.InlineOperationsForTests)
                           {
                               // Inlining is used only for testing purposes. In those cases,
                               // we only perform one proactive replication.
                               break;
                           }

                           await delayTask;
                       }

                       return BoolResult.Success;
                   })
                .FireAndForgetOrInlineAsync(context, _settings.InlineOperationsForTests);
        }

        private Task<ProactiveReplicationResult> ProactiveReplicationIterationAsync(
            OperationContext context,
            ReadOnlyDistributedContentSession<T> proactiveCopySession,
            ILocalContentStore localContentStore,
            TransitioningContentLocationStore contentLocationStore)
        {
            return context.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    // Important to yield as GetContentInfoAsync has a synchronous implementation.
                    await Task.Yield();

                    var localContent = (await localContentStore.GetContentInfoAsync(context.Token))
                        .OrderByDescending(info => info.LastAccessTimeUtc) // GetHashesInEvictionOrder expects entries to already be ordered by last access time.
                        .Select(info => new ContentHashWithLastAccessTimeAndReplicaCount(info.ContentHash, info.LastAccessTimeUtc))
                        .ToArray();

                    var contents = contentLocationStore.GetHashesInEvictionOrder(context, localContent, reverse: true);

                    var succeeded = 0;
                    var failed = 0;
                    var skipped = 0;
                    var scanned = 0;
                    var rejected = 0;
                    var delayTask = Task.CompletedTask;
                    var wasPreviousCopyNeeded = true;
                    ContentEvictionInfo? lastVisited = default;

                    IEnumerable<ContentEvictionInfo> getReplicableHashes()
                    {
                        foreach (var content in contents)
                        {
                            scanned++;

                            if (content.ReplicaCount < _settings.ProactiveCopyLocationsThreshold)
                            {
                                yield return content;
                            }
                            else
                            {
                                CounterCollection[Counters.ProactiveReplication_Skipped].Increment();
                                skipped++;
                            }
                        }
                    }

                    foreach (var page in getReplicableHashes().GetPages(_settings.ProactiveCopyGetBulkBatchSize))
                    {
                        var contentInfos = await proactiveCopySession.GetLocationsForProactiveCopyAsync(context, page.SelectList(c => c.ContentHash));
                        for (int i = 0; i < contentInfos.Count; i++)
                        {
                            context.Token.ThrowIfCancellationRequested();

                            var contentInfo = contentInfos[i];
                            lastVisited = page[i];

                            if (wasPreviousCopyNeeded)
                            {
                                await delayTask;
                                delayTask = Task.Delay(_settings.DelayForProactiveReplication, context.Token);
                            }

                            var result = await proactiveCopySession.ProactiveCopyIfNeededAsync(
                                context,
                                contentInfo,
                                tryBuildRing: false,
                                reason: ProactiveCopyReason.Replication);

                            wasPreviousCopyNeeded = true;
                            switch (result.Status)
                            {
                                case ProactiveCopyStatus.Success:
                                    CounterCollection[Counters.ProactiveReplication_Succeeded].Increment();
                                    succeeded++;
                                    break;
                                case ProactiveCopyStatus.Skipped:
                                    CounterCollection[Counters.ProactiveReplication_Skipped].Increment();
                                    skipped++;
                                    wasPreviousCopyNeeded = false;
                                    break;
                                case ProactiveCopyStatus.Rejected:
                                    rejected++;
                                    CounterCollection[Counters.ProactiveReplication_Rejected].Increment();
                                    break;
                                case ProactiveCopyStatus.Error:
                                    CounterCollection[Counters.ProactiveReplication_Failed].Increment();
                                    failed++;
                                    break;
                            }

                            if ((succeeded + failed) >= _settings.ProactiveReplicationCopyLimit)
                            {
                                break;
                            }
                        }

                        if ((succeeded + failed) >= _settings.ProactiveReplicationCopyLimit)
                        {
                            break;
                        }
                    }

                    return new ProactiveReplicationResult(succeeded, failed, skipped, rejected, localContent.Length, scanned, lastVisited);
                },
                counter: CounterCollection[Counters.ProactiveReplication]);
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            var results = new List<(string operation, BoolResult result)>();

            if (ProactiveCopySession?.IsValueCreated == true)
            {
                var sessionResult = await ProactiveCopySession.Value;
                if (sessionResult.Succeeded)
                {
                    var proactiveCopySessionShutdownResult = await sessionResult.Value.ShutdownAsync(context);
                    results.Add((nameof(ProactiveCopySession), proactiveCopySessionShutdownResult));
                }
            }

            var innerResult = await InnerContentStore.ShutdownAsync(context);
            results.Add((nameof(InnerContentStore), innerResult));

            if (_contentLocationStore != null)
            {
                var locationStoreResult = await _contentLocationStore.ShutdownAsync(context);
                results.Add((nameof(_contentLocationStore), locationStoreResult));
            }

            var factoryResult = await _contentLocationStoreFactory.ShutdownAsync(context);
            results.Add((nameof(_contentLocationStoreFactory), factoryResult));

            _copierWorkingDirectory.Dispose();

            return ShutdownErrorCompiler(results);
        }

        /// <inheritdoc />
        public CreateSessionResult<IReadOnlyContentSession> CreateReadOnlySession(Context context, string name, ImplicitPin implicitPin)
        {
            return CreateReadOnlySessionCall.Run(_tracer, OperationContext(context), name, () =>
            {
                CreateSessionResult<IContentSession> innerSessionResult = InnerContentStore.CreateSession(context, name, implicitPin);

                if (innerSessionResult.Succeeded)
                {
                    var session = new ReadOnlyDistributedContentSession<T>(
                            name,
                            innerSessionResult.Session,
                            _contentLocationStore,
                            _distributedCopier,
                            this,
                            LocalMachineLocation,
                            settings: _settings);
                    return new CreateSessionResult<IReadOnlyContentSession>(session);
                }

                return new CreateSessionResult<IReadOnlyContentSession>(innerSessionResult, "Could not initialize inner content session with error");
            });
        }

        /// <inheritdoc />
        public CreateSessionResult<IContentSession> CreateSession(Context context, string name, ImplicitPin implicitPin)
        {
            return CreateSessionCall.Run(_tracer, OperationContext(context), name, () =>
            {
                CreateSessionResult<IContentSession> innerSessionResult = InnerContentStore.CreateSession(context, name, implicitPin);

                if (innerSessionResult.Succeeded)
                {
                    var session = new DistributedContentSession<T>(
                            name,
                            innerSessionResult.Session,
                            _contentLocationStore,
                            _distributedCopier,
                            this,
                            LocalMachineLocation,
                            settings: _settings);
                    return new CreateSessionResult<IContentSession>(session);
                }

                return new CreateSessionResult<IContentSession>(innerSessionResult, "Could not initialize inner content session with error");
            });
        }

        /// <inheritdoc />
        public Task<GetStatsResult> GetStatsAsync(Context context)
        {
            return GetStatsCall<ContentStoreTracer>.RunAsync(_tracer, OperationContext(context), async () =>
            {
                var result = await InnerContentStore.GetStatsAsync(context);
                if (result.Succeeded)
                {
                    var counterSet = result.CounterSet;
                    if (_contentLocationStore != null)
                    {
                        var contentLocationStoreCounters = _contentLocationStore.GetCounters(context);
                        counterSet.Merge(contentLocationStoreCounters, "ContentLocationStore.");
                    }

                    return new GetStatsResult(counterSet);
                }

                return result;
            });
        }

        /// <summary>
        /// Remove local location from the content tracker.
        /// </summary>
        public async Task<StructResult<long>> RemoveFromTrackerAsync(Context context)
        {
            if (_settings.EnableRepairHandling)
            {
                var result = await _contentLocationStore.InvalidateLocalMachineAsync(context, CancellationToken.None);
                if (!result)
                {
                    return new StructResult<long>(result);
                }
            }

            // New logic doesn't have the content removed count
            return StructResult.Create((long)0);
        }

        /// <summary>
        /// Determines if final BoolResult is success or error.
        /// </summary>
        private static BoolResult ShutdownErrorCompiler(IReadOnlyList<(string operation, BoolResult result)> results)
        {
            var sb = new StringBuilder();
            foreach (var (operation, result) in results)
            {
                if (!result)
                {
                    // TODO: Consider compiling Item2's Diagnostics into the final result's Diagnostics instead of ErrorMessage (bug 1365340)
                    sb.Append($"{operation}: {result} ");
                }
            }

            return sb.Length != 0 ? new BoolResult(sb.ToString()) : BoolResult.Success;
        }

        /// <nodoc />
        protected override void DisposeCore()
        {
            InnerContentStore.Dispose();

            if (DisposeContentStoreFactory)
            {
                _contentLocationStoreFactory.Dispose();
            }
        }

        /// <nodoc />
        public bool CanComputeLru => (_contentLocationStore as IDistributedLocationStore)?.CanComputeLru ?? false;

        /// <nodoc />
        public Task<BoolResult> UnregisterAsync(Context context, IReadOnlyList<ContentHash> contentHashes, CancellationToken token, TimeSpan? minEffectiveAge = null)
        {
            if (InnerContentStore is ILocalContentStore localContentStore)
            {
                // Filter out hashes which exist in the local content store (may have been re-added by a recent put).
                var filteredHashes = contentHashes.Where(hash => !localContentStore.Contains(hash)).ToList();
                if (filteredHashes.Count != contentHashes.Count)
                {
                    Tracer.OperationDebug(context, $"Hashes not unregistered because they are still present in local store: [{string.Join(",", contentHashes.Except(filteredHashes))}]");
                    contentHashes = filteredHashes;
                }
            }

            if (_settings.ProactiveCopyRejectOldContent && minEffectiveAge != null)
            {
                _lastEvictedEffectiveLastAccessTime = _clock.UtcNow - minEffectiveAge;
            }

            return _contentLocationStore.TrimBulkAsync(context, contentHashes, token, UrgencyHint.Nominal);
        }

        /// <nodoc />
        public IEnumerable<ContentEvictionInfo> GetHashesInEvictionOrder(Context context, IReadOnlyList<ContentHashWithLastAccessTimeAndReplicaCount> contentHashesWithInfo)
        {
            // Ensure startup was called then wait for it to complete successfully (or error)
            // This logic is important to avoid runtime errors when, for instance, QuotaKeeper tries
            // to evict content right after startup and calls GetLruPages.
            Contract.Assert(StartupStarted);
            WaitForPostInitializationCompletionIfNeeded(context);

            Contract.Assert(_contentLocationStore is IDistributedLocationStore);
            if (_contentLocationStore is IDistributedLocationStore distributedStore)
            {
                return distributedStore.GetHashesInEvictionOrder(context, contentHashesWithInfo);
            }
            else
            {
                throw Contract.AssertFailure($"Cannot call GetLruPages when CanComputeLru returns false");
            }
        }

        private void WaitForPostInitializationCompletionIfNeeded(Context context)
        {
            var task = _postInitializationCompletion.Task;
            if (!task.IsCompleted)
            {
                var operationContext = new OperationContext(context);
                operationContext.PerformOperation(Tracer, () => waitForCompletion(), traceOperationStarted: false).ThrowIfFailure();
            }

            BoolResult waitForCompletion()
            {
                context.Debug($"Post-initialization is not done. Waiting for it to finish...");
                return task.GetAwaiter().GetResult();
            }
        }

        /// <summary>
        /// Attempts to get local location store if enabled
        /// </summary>
        public bool TryGetLocalLocationStore(out LocalLocationStore localLocationStore)
        {
            if (_contentLocationStore is TransitioningContentLocationStore tcs)
            {
                localLocationStore = tcs.LocalLocationStore;
                return true;
            }

            localLocationStore = null;
            return false;
        }

        /// <summary>
        /// Gets the associated local location store instance
        /// </summary>
        public LocalLocationStore LocalLocationStore => (_contentLocationStore as TransitioningContentLocationStore)?.LocalLocationStore;

        /// <summary>
        /// Checks the LLS <see cref="DistributedCentralStorage"/> for the content if available and returns
        /// the storage instance if content is found
        /// </summary>
        private bool CheckLlsForContent(ContentHash desiredContent, out DistributedCentralStorage storage)
        {
            if (_contentLocationStore is TransitioningContentLocationStore tcs
                && tcs.LocalLocationStore.DistributedCentralStorage != null
                && tcs.LocalLocationStore.DistributedCentralStorage.HasContent(desiredContent))
            {
                storage = tcs.LocalLocationStore.DistributedCentralStorage;
                return true;
            }

            storage = default;
            return false;
        }

        /// <inheritdoc />
        public async Task<OpenStreamResult> StreamContentAsync(Context context, ContentHash contentHash)
        {
            // NOTE: Checking LLS for content needs to happen first since the query to the inner stream store result
            // is used even if the result is fails.
            if (CheckLlsForContent(contentHash, out var storage))
            {
                var result = await storage.StreamContentAsync(context, contentHash);
                if (result.Succeeded)
                {
                    return result;
                }
            }

            if (InnerContentStore is IStreamStore innerStreamStore)
            {
                return await innerStreamStore.StreamContentAsync(context, contentHash);
            }

            return new OpenStreamResult($"{InnerContentStore} does not implement {nameof(IStreamStore)} in {nameof(DistributedContentStore<T>)}.");
        }

        /// <inheritdoc />
        public async Task<FileExistenceResult> CheckFileExistsAsync(Context context, ContentHash contentHash)
        {
            // NOTE: Checking LLS for content needs to happen first since the query to the inner stream store result
            // is used even if the result is fails.
            if (CheckLlsForContent(contentHash, out var storage))
            {
                return new FileExistenceResult(FileExistenceResult.ResultCode.FileExists);
            }

            if (InnerContentStore is IStreamStore innerStreamStore)
            {
                return await innerStreamStore.CheckFileExistsAsync(context, contentHash);
            }

            return new FileExistenceResult(FileExistenceResult.ResultCode.Error, $"{InnerContentStore} does not implement {nameof(IStreamStore)} in {nameof(DistributedContentStore<T>)}.");
        }

        Task<DeleteResult> IDeleteFileHandler.HandleDeleteAsync(Context context, ContentHash contentHash, DeleteContentOptions deleteOptions) => DeleteAsync(context, contentHash, deleteOptions);

        /// <inheritdoc />
        public Task<DeleteResult> DeleteAsync(Context context, ContentHash contentHash, DeleteContentOptions deleteOptions)
        {
            var operationContext = OperationContext(context);
            deleteOptions ??= new DeleteContentOptions() {DeleteLocalOnly = true};

            return operationContext.PerformOperationAsync(
                Tracer,
                async () =>
                {
                    var deleteResult = await InnerContentStore.DeleteAsync(context, contentHash, deleteOptions);
                    var contentHashes = new ContentHash[] {contentHash};
                    if (!deleteResult)
                    {
                        return deleteResult;
                    }

                    // Tell the event hub that this machine has removed the content locally
                    var unRegisterResult = await UnregisterAsync(context, contentHashes, operationContext.Token).ThrowIfFailure();
                    if (!unRegisterResult)
                    {
                        return new DeleteResult(unRegisterResult, unRegisterResult.ToString());
                    }

                    if (deleteOptions.DeleteLocalOnly)
                    {
                        return deleteResult;
                    }

                    var deleteResultsMapping = new Dictionary<string, DeleteResult>();

                    var result = await _contentLocationStore.GetBulkAsync(
                        context,
                        contentHashes,
                        operationContext.Token,
                        UrgencyHint.Nominal,
                        GetBulkOrigin.Local);
                    if (!result)
                    {
                        deleteResult =  new DeleteResult(result, result.ToString());
                        deleteResultsMapping.Add(LocalMachineLocation.Path, deleteResult);
                        return new DistributedDeleteResult(contentHash, deleteResult.ContentSize, deleteResultsMapping);
                    }

                    deleteResultsMapping.Add(LocalMachineLocation.Path, deleteResult);

                    // Go through each machine that has this content, and delete async locally on each machine.
                    if (result.ContentHashesInfo[0].Locations != null)
                    {
                        var machineLocations = result.ContentHashesInfo[0].Locations;
                        return await _distributedCopier.DeleteAsync(operationContext, contentHash, deleteResult.ContentSize, machineLocations, deleteResultsMapping);
                    }

                    return new DistributedDeleteResult(contentHash, deleteResult.ContentSize, deleteResultsMapping);
                });
        }

        /// <inheritdoc />
        public Task<BoolResult> HandleCopyFileRequestAsync(Context context, ContentHash hash, CancellationToken token)
        {
            using var shutdownTracker = TrackShutdown(context, token);
            var operationContext = shutdownTracker.Context;
            return operationContext.PerformOperationAsync(Tracer,
                async () =>
                {
                    var session = await ProactiveCopySession.Value.ThrowIfFailureAsync();
                    using (await session.OpenStreamAsync(context, hash, operationContext.Token).ThrowIfFailureAsync(o => o.Stream))
                    {
                        // Opening stream to ensure the content is copied locally. Stream is immediately disposed.
                    }

                    return BoolResult.Success;
                },
                traceOperationStarted: false,
                extraEndMessage: _ => $"Hash=[{hash.ToShortString()}]");
        }

        /// <inheritdoc />
        public async Task<PutResult> HandlePushFileAsync(Context context, ContentHash hash, AbsolutePath sourcePath, CancellationToken token)
        {
            if (InnerContentStore is IPushFileHandler inner)
            {
                var result = await inner.HandlePushFileAsync(context, hash, sourcePath, token);
                if (!result)
                {
                    return result;
                }

                var registerResult = await _contentLocationStore.RegisterLocalLocationAsync(context, new[] { new ContentHashWithSize(hash, result.ContentSize) }, token, UrgencyHint.Nominal, touch: false);
                if (!registerResult)
                {
                    return new PutResult(registerResult);
                }

                return result;
            }

            return new PutResult(new InvalidOperationException($"{nameof(InnerContentStore)} does not implement {nameof(IPushFileHandler)}"), hash);
        }

        /// <inheritdoc />
        public bool CanAcceptContent(Context context, ContentHash hash, out RejectionReason rejectionReason)
        {
            if (InnerContentStore is IPushFileHandler inner)
            {
                if (!inner.CanAcceptContent(context, hash, out rejectionReason))
                {
                    return false;
                }
            }

            if (_settings.ProactiveCopyRejectOldContent)
            {
                var operationContext = OperationContext(context);
                if (TryGetLocalLocationStore(out var lls) && _contentLocationStore is TransitioningContentLocationStore tcs)
                {
                    if (lls.Database.TryGetEntry(operationContext, hash, out var entry))
                    {
                        var effectiveLastAccessTimeResult =
                            lls.GetEffectiveLastAccessTimes(operationContext, tcs, new ContentHashWithLastAccessTime[] { new ContentHashWithLastAccessTime(hash, entry.LastAccessTimeUtc.ToDateTime()) });
                        if (effectiveLastAccessTimeResult)
                        {
                            var effectiveAge = effectiveLastAccessTimeResult.Value[0].EffectiveAge;
                            var effectiveLastAccessTime = _clock.UtcNow - effectiveAge;
                            if (_lastEvictedEffectiveLastAccessTime > effectiveLastAccessTime == true)
                            {
                                CounterCollection[Counters.RejectedPushCopyCount_OlderThanEvicted].Increment();
                                rejectionReason = RejectionReason.OlderThanLastEvictedContent;
                                return false;
                            }
                        }
                    }
                }
            }

            rejectionReason = RejectionReason.Accepted;
            return true;
        }
    }
}
