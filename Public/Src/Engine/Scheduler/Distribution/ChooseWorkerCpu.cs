// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Pips;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Artifacts;
using BuildXL.Scheduler.Tracing;
using BuildXL.Scheduler.WorkDispatcher;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Scheduler.Distribution
{
    /// <summary>
    /// Handles choose worker computation for <see cref="Scheduler"/>
    /// </summary>
    internal class ChooseWorkerCpu : ChooseWorkerContext
    {
        /// <summary>
        /// Use this constant to guess the missing file size.
        /// </summary>
        private const long TypicalFileLength = 4096;

        private const double MaxLoadFactor = 2;

        private readonly ReadOnlyArray<double> m_workerBalancedLoadFactors =

            // Workers are given progressively heavier loads when acquiring resources
            // in order to load balance between workers by
            ReadOnlyArray<double>.FromWithoutCopy(0.25, 0.5, 1, 1.5, MaxLoadFactor);

        private readonly FileContentManager m_fileContentManager;

        private readonly PipTable m_pipTable;

        private readonly ObjectPool<PipSetupCosts> m_pipSetupCostPool;

        private readonly SemaphoreSlim m_chooseWorkerMutex = TaskUtilities.CreateMutex();

        private RunnablePip m_lastIterationBlockedPip;

        /// <summary>
        /// The last pip blocked on resources
        /// </summary>
        public RunnablePip LastBlockedPip { get; private set; }

        /// <summary>
        /// The last resource limiting acquisition of a worker. 
        /// </summary>
        /// <remarks>
        /// If it is null, there is no resource limiting the worker.
        /// </remarks>
        public WorkerResource? LastConcurrencyLimiter { get; set; }

        /// <summary>
        /// The number of choose worker iterations
        /// </summary>
        public int ChooseIterations { get; private set; }

        /// <summary>
        /// The total time spent choosing a worker
        /// </summary>
        public int ChooseSeconds => (int)m_chooseTime.TotalSeconds;

        private TimeSpan m_chooseTime = TimeSpan.Zero;

        /// <summary>
        /// TEMPORARY HACK: Tracks outputs of executed processes which are the sole considered files for IPC pip affinity. This is intended to
        /// address a bug where IPC pips can disproportionately get assigned to a worker simply because it has materialized
        /// cached inputs
        /// </summary>
        private readonly ContentTrackingSet m_executedProcessOutputs;

        protected readonly Dictionary<WorkerResource, BoxRef<int>> m_limitingResourceCounts = new Dictionary<WorkerResource, BoxRef<int>>();

        private int m_totalAcquiredProcessSlots;

        private int m_totalProcessSlots;

        private readonly bool m_enableSetupCost;

        public ChooseWorkerCpu(
            LoggingContext loggingContext,
            int maxParallelDegree,
            bool enableSetupCostWhenChoosingWorker,
            IReadOnlyList<Worker> workers,
            IPipQueue pipQueue,
            PipGraph pipGraph,
            FileContentManager fileContentManager) : base(loggingContext, workers, pipQueue, DispatcherKind.ChooseWorkerCpu, maxParallelDegree)
        {
            m_pipTable = pipGraph.PipTable;
            m_executedProcessOutputs = new ContentTrackingSet(pipGraph);
            m_fileContentManager = fileContentManager;
            m_pipSetupCostPool = new ObjectPool<PipSetupCosts>(() => new PipSetupCosts(this), costs => costs, size: maxParallelDegree);
            m_enableSetupCost = enableSetupCostWhenChoosingWorker;
        }

        /// <summary>
        /// Reports the outputs of process execution after post process to distinguish between produced outputs and
        /// outputs from cache when assigning affinity for IPC pips
        /// </summary>
        public void ReportProcessExecutionOutputs(ProcessRunnablePip runnableProcess, ExecutionResult executionResult)
        {
            Contract.Assert(runnableProcess.Step == PipExecutionStep.PostProcess);

            if (executionResult.Converged || runnableProcess.Process.IsStartOrShutdownKind)
            {
                // Converged results are cached so they are not considered for as execution outputs
                // Service start/shutdown pip outputs are not considered for IPC pip affinity
                return;
            }

            foreach (var directoryOutput in executionResult.DirectoryOutputs)
            {
                m_executedProcessOutputs.Add(directoryOutput.directoryArtifact);
            }

            foreach (var output in executionResult.OutputContent)
            {
                m_executedProcessOutputs.Add(output.fileArtifact);
            }
        }

        /// <summary>
        /// Choose a worker based on setup cost
        /// </summary>
        protected override async Task<Worker> ChooseWorkerCore(RunnablePip runnablePip)
        {
            using (var pooledPipSetupCost = m_pipSetupCostPool.GetInstance())
            {
                var pipSetupCost = pooledPipSetupCost.Instance;
                if (m_enableSetupCost)
                {
                    pipSetupCost.EstimateAndSortSetupCostPerWorker(runnablePip);
                }
                else
                {
                    pipSetupCost.InitializeWorkerSetupCost(runnablePip.Pip);
                }

                using (await m_chooseWorkerMutex.AcquireAsync())
                {
                    var startTime = TimestampUtilities.Timestamp;
                    ChooseIterations++;

                    WorkerResource? limitingResource;
                    var chosenWorker = ChooseWorker(runnablePip, pipSetupCost.WorkerSetupCosts, out limitingResource);
                    if (chosenWorker == null)
                    {
                        m_lastIterationBlockedPip = runnablePip;
                        LastBlockedPip = runnablePip;
                        var limitingResourceCount = m_limitingResourceCounts.GetOrAdd(limitingResource.Value, k => new BoxRef<int>());
                        limitingResourceCount.Value++;
                    }
                    else
                    {
                        m_lastIterationBlockedPip = null;
                    }
                    
                    // If a worker is successfully chosen, then the limiting resouce would be null.
                    LastConcurrencyLimiter = limitingResource;

                    m_chooseTime += TimestampUtilities.Timestamp - startTime;
                    return chosenWorker;
                }
            }
        }

        /// <summary>
        /// Choose a worker based on setup cost
        /// </summary>
        private Worker ChooseWorker(RunnablePip runnablePip, WorkerSetupCost[] workerSetupCosts, out WorkerResource? limitingResource)
        {
            if (MustRunOnMaster(runnablePip))
            {
                // This is shortcut for the single-machine builds and distributed workers.
                return LocalWorker.TryAcquire(runnablePip, out limitingResource, loadFactor: MaxLoadFactor) ? LocalWorker : null;
            }

            ResetStatus();

            var pendingWorkerSelectionPipCount = PipQueue.GetNumQueuedByKind(DispatcherKind.ChooseWorkerCpu) + PipQueue.GetNumRunningByKind(DispatcherKind.ChooseWorkerCpu);

            bool loadBalanceWorkers = false;
            if (runnablePip.PipType == PipType.Process)
            {
                if (pendingWorkerSelectionPipCount + m_totalAcquiredProcessSlots < (m_totalProcessSlots / 2))
                {
                    // When there is a limited amount of work (less than half the total capacity of
                    // the all the workers). We load balance so that each worker gets
                    // its share of the work and the work can complete faster
                    loadBalanceWorkers = true;
                }
            }

            double? disableLoadBalanceMultiplier = EngineEnvironmentSettings.DisableLoadBalanceMultiplier;

            // Disable load-balance if there is a multiplier specified including 0.
            loadBalanceWorkers &= !disableLoadBalanceMultiplier.HasValue;

            long setupCostForBestWorker = workerSetupCosts[0].SetupBytes;

            limitingResource = null;
            foreach (var loadFactor in m_workerBalancedLoadFactors)
            {
                if (!loadBalanceWorkers && loadFactor < 1)
                {
                    // Not load balancing so allow worker to be filled to capacity at least
                    continue;
                }

                for (int i = 0; i < workerSetupCosts.Length; i++)
                {
                    var worker = workerSetupCosts[i].Worker;
                    if (worker.TryAcquire(runnablePip, out limitingResource, loadFactor: loadFactor))
                    {
                        runnablePip.Performance.SetInputMaterializationCost(ByteSizeFormatter.ToMegabytes((ulong)setupCostForBestWorker), ByteSizeFormatter.ToMegabytes((ulong)workerSetupCosts[i].SetupBytes));
                        return worker;
                    }

                    // If the worker is not chosen due to the lack of process slots,
                    // do not try the next worker immediately if 'BuildXLDisableLoadBalanceMultiplier' is specified.
                    // We first check whether the number of pips waiting for a worker is less than the total slots times with the multiplier.
                    // For example, if the multiplier is 1 and totalWorkerSlots is 100, then we do not try the next worker 
                    // if there are less than 100 pips waiting for a worker. 
                    // For Cosine builds, executing pips on a new worker is expensive due to the input materialization.
                    // It is usually faster to wait for the busy worker to be available compared to trying on another worker.
                    if (limitingResource == WorkerResource.AvailableProcessSlots && 
                        disableLoadBalanceMultiplier.HasValue &&
                        pendingWorkerSelectionPipCount < (worker.TotalProcessSlots * disableLoadBalanceMultiplier.Value))
                    {
                        limitingResource = WorkerResource.DisableLoadBalance;
                        return null;
                    }
                }
            }

            return null;
        }

        protected void ResetStatus()
        {
            m_totalAcquiredProcessSlots = 0;
            m_totalProcessSlots = 0;

            for (int i = 0; i < Workers.Count; i++)
            {
                if (Workers[i].IsAvailable)
                {
                    m_totalAcquiredProcessSlots += Workers[i].AcquiredProcessSlots;
                    m_totalProcessSlots += Workers[i].TotalProcessSlots;
                }
            }
        }

        internal void UnpauseChooseWorkerQueueIfEnqueuingNewPip(RunnablePip runnablePip, DispatcherKind nextQueue)
        {
            // If enqueuing a new highest priority pip to queue
            if (nextQueue == DispatcherKind.ChooseWorkerCpu)
            {
                if (runnablePip != m_lastIterationBlockedPip)
                {
                    TogglePauseChooseWorkerQueue(pause: false);
                }

                // Capture the sequence number which will be used to compare if ChooseWorker queue is paused
                // waiting for resources for this pip to avoid race conditions where pip cannot acquire worker
                // resources become available then queue is paused potentially indefinitely (not likely but theoretically
                // possilbe)
                runnablePip.ChooseWorkerSequenceNumber = Volatile.Read(ref WorkerEnableSequenceNumber);
            }
        }

        public void LogStats(Dictionary<string, long> statistics)
        {
            var limitingResourceStats = m_limitingResourceCounts.ToDictionary(kvp => kvp.Key.ToString(), kvp => (long)kvp.Value.Value);

            foreach (var kvp in limitingResourceStats)
            {
                statistics.Add($"LimitingResource_{kvp.Key}", kvp.Value);
            }

            Logger.Log.LimitingResourceStatistics(LoggingContext, limitingResourceStats);
        }

        private class PipSetupCosts
        {
            public readonly WorkerSetupCost[] WorkerSetupCosts;
            private readonly ChooseWorkerCpu m_context;
            private readonly HashSet<ContentHash> m_visitedHashes = new HashSet<ContentHash>();

            public PipSetupCosts(ChooseWorkerCpu context)
            {
                m_context = context;
                WorkerSetupCosts = new WorkerSetupCost[context.Workers.Count];
            }

            /// <summary>
            /// The result contains estimated amount of work for each worker
            /// </summary>
            public void EstimateAndSortSetupCostPerWorker(RunnablePip runnablePip)
            {
                if (m_context.MustRunOnMaster(runnablePip))
                {
                    // Only estimate setup costs for pips which can execute remotely
                    return;
                }

                var pip = runnablePip.Pip;

                InitializeWorkerSetupCost(pip);

                // The block below collects process input file artifacts and hashes
                // Currently there is no logic to keep from sending the same hashes twice
                // Consider a model where hashes for files are requested by worker
                using (var pooledFileSet = Pools.GetFileArtifactSet())
                {
                    var pipInputs = pooledFileSet.Instance;
                    m_context.m_fileContentManager.CollectPipInputsToMaterialize(
                        m_context.m_pipTable,
                        pip,
                        pipInputs,

                        // Service pip cost is not considered as this is shared among many clients and is a one-time cost per worker
                        serviceFilter: servicePipId => false);

                    m_visitedHashes.Clear();

                    foreach (var fileInput in pipInputs)
                    {
                        if (!fileInput.IsOutputFile)
                        {
                            continue;
                        }

                        if (pip.PipType == PipType.Ipc && !m_context.m_executedProcessOutputs.Contains(fileInput))
                        {
                            // Only executed process outputs are considered for IPC pip affinity
                            continue;
                        }

                        FileContentInfo fileContentInfo = m_context.m_fileContentManager.GetInputContent(fileInput).FileContentInfo;
                        if (!m_visitedHashes.Add(fileContentInfo.Hash))
                        {
                            continue;
                        }

                        // How many bytes we have to copy.
                        long fileSize = fileContentInfo.HasKnownLength ? fileContentInfo.Length : TypicalFileLength;

                        for (int idx = 0; idx < m_context.Workers.Count; ++idx)
                        {
                            if (!WorkerSetupCosts[idx].Worker.HasContent(fileInput))
                            {
                                WorkerSetupCosts[idx].SetupBytes += fileSize;
                            }
                        }
                    }
                }

                Array.Sort(WorkerSetupCosts);
            }

            public void InitializeWorkerSetupCost(Pip pip)
            {
                for (int i = 0; i < m_context.Workers.Count; i++)
                {
                    var worker = m_context.Workers[i];
                    WorkerSetupCosts[i] = new WorkerSetupCost()
                    {
                        Worker = worker,
                        AcquiredSlots = pip.PipType == PipType.Ipc ? worker.AcquiredIpcSlots : worker.AcquiredProcessSlots
                    };
                }
            }
        }

        /// <summary>
        /// Worker setup cost structure.
        /// </summary>
        private struct WorkerSetupCost : IComparable<WorkerSetupCost>
        {
            /// <summary>
            /// Total bytes that need to be setup.
            /// </summary>
            public long SetupBytes { get; set; }

            /// <summary>
            /// Number of acquired slots
            /// </summary>
            /// <remarks>
            /// For IPC pips, this means acquired IPC slots, and for process pips, it means acquired process slots.
            /// </remarks>
            public int AcquiredSlots { get; set; }

            /// <summary>
            /// The associated worker
            /// </summary>
            public Worker Worker { get; set; }

            /// <inheritdoc />
            public int CompareTo(WorkerSetupCost other)
            {
                var result = SetupBytes.CompareTo(other.SetupBytes);
                return result == 0 ? AcquiredSlots.CompareTo(other.AcquiredSlots) : result;
            }
        }
    }
}
