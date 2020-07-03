// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Interop;
using BuildXL.Pips;
using BuildXL.Scheduler;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Scheduler
{
    public sealed class ProcessResourceManagerTests : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {
        private int m_nextId;

        public ProcessResourceManagerTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public async Task TestResourceManagerCancellationPreference()
        {
            ProcessResourceManager resourceManager = new ProcessResourceManager(null);

            var workItem1 = CreateWorkItem(resourceManager, estimatedRamUsage: 1, reportedRamUsage: 1);

            // Highest RAM usage over estimate
            var workItem2 = CreateWorkItem(resourceManager, estimatedRamUsage: 1, reportedRamUsage: 1002);

            // Higest overall RAM usage, second most recently executed (cancelled second)
            var workItem3 = CreateWorkItem(resourceManager, estimatedRamUsage: 1000, reportedRamUsage: 2000);

            // Most recently executed (Cancelled second)
            var workItem4 = CreateWorkItem(resourceManager, estimatedRamUsage: 1, reportedRamUsage: 1);

            var workItem4Cancelled = workItem4.WaitForCancellation();
            RunResourceManager(resourceManager, requiredSizeMb: 1, mode: ManageMemoryMode.CancellationRam);

            // Ensure only work item 4 was cancelled since that is all that is required to free necessary RAM
            XAssert.IsTrue(await workItem4Cancelled);
            workItem1.Verify(expectedCancellationCount: 0, expectedExecutionCount: 1, expectedCompleted: false);
            workItem2.Verify(expectedCancellationCount: 0, expectedExecutionCount: 1, expectedCompleted: false);
            workItem3.Verify(expectedCancellationCount: 0, expectedExecutionCount: 1, expectedCompleted: false);
            workItem4.Verify(expectedCancellationCount: 1, expectedExecutionCount: 1, expectedCompleted: false);

            var workItem3Cancelled = workItem3.WaitForCancellation();
            RunResourceManager(resourceManager, requiredSizeMb: 1000, mode: ManageMemoryMode.CancellationRam);

            // Ensure only work item 3 was cancelled since that is all that is required to free necessary RAM
            XAssert.IsTrue(await workItem3Cancelled);
            workItem1.Verify(expectedCancellationCount: 0, expectedExecutionCount: 1, expectedCompleted: false);
            workItem2.Verify(expectedCancellationCount: 0, expectedExecutionCount: 1, expectedCompleted: false);
            workItem3.Verify(expectedCancellationCount: 1, expectedExecutionCount: 1, expectedCompleted: false);
            workItem4.Verify(expectedCancellationCount: 1, expectedExecutionCount: 1, expectedCompleted: false);
        }

        [Fact]
        public async Task TestResourceManagerCancellationPreferenceMultiple()
        {
            ProcessResourceManager resourceManager = new ProcessResourceManager(null);

            // Highest RAM, but oldest pip so it is not cancelled
            var workItem1 = CreateWorkItem(resourceManager, estimatedRamUsage: 1, reportedRamUsage: 10000);

            var workItem2 = CreateWorkItem(resourceManager, estimatedRamUsage: 1, reportedRamUsage: 1002);

            var workItem3 = CreateWorkItem(resourceManager, estimatedRamUsage: 1000, reportedRamUsage: 2000);

            var workItem4 = CreateWorkItem(resourceManager, estimatedRamUsage: 1, reportedRamUsage: 1);

            var workItem3Cancelled = workItem3.WaitForCancellation();
            var workItem2Cancelled = workItem2.WaitForCancellation();
            var workItem4Cancelled = workItem4.WaitForCancellation();

            // Attempt to free more RAM than occupied by all work items (oldest pips should be retained even though
            // it must be freed to attempt to meet resource requirements)
            RunResourceManager(resourceManager, requiredSizeMb: 20000, mode: ManageMemoryMode.CancellationRam);

            // Ensure only work item 2 AND 3 were cancelled since that is all that is required to free necessary RAM
            XAssert.IsTrue(await workItem2Cancelled);
            XAssert.IsTrue(await workItem3Cancelled);
            XAssert.IsTrue(await workItem4Cancelled);
            workItem1.Verify(expectedCancellationCount: 0, expectedExecutionCount: 1, expectedCompleted: false);
            workItem2.Verify(expectedCancellationCount: 1, expectedExecutionCount: 1, expectedCompleted: false);
            workItem3.Verify(expectedCancellationCount: 1, expectedExecutionCount: 1, expectedCompleted: false);
            workItem4.Verify(expectedCancellationCount: 1, expectedExecutionCount: 1, expectedCompleted: false);
        }

        private void RunResourceManager(ProcessResourceManager resourceManager, int requiredSizeMb, ManageMemoryMode mode)
        {
            resourceManager.RefreshMemoryCounters();
            resourceManager.TryManageResources(requiredSizeMb, mode);
        }

        private ResourceManagerWorkItemTracker CreateWorkItem(ProcessResourceManager resourceManager, int estimatedRamUsage = 0, bool allowCancellation = true, int reportedRamUsage = 1)
        {
            return new ResourceManagerWorkItemTracker(
                LoggingContext, 
                resourceManager, 
                (uint)Interlocked.Increment(ref m_nextId), 
                estimatedRamUsage, 
                allowCancellation)
            { 
                RamUsage = ProcessMemoryCountersSnapshot.CreateFromMB(reportedRamUsage, reportedRamUsage, reportedRamUsage, reportedRamUsage, reportedRamUsage)
            };
        }

        private class ResourceManagerWorkItemTracker
        {
            public const int CancelledResult = -1;

            public int ExecutionCount { get; private set; }
            public int CancellationCount { get; private set; }
            public readonly TaskSourceSlim<int> ExecutionCompletionSource;
            private TaskSourceSlim<Unit> m_cancellationCompletionSource;
            private readonly SemaphoreSlim m_startSemaphore = new SemaphoreSlim(0, 1);
            public Task<int> ExecutionTask { get; private set; }
            private readonly Func<Task<int>> m_execute;

            public ProcessMemoryCountersSnapshot RamUsage = ProcessMemoryCountersSnapshot.CreateFromMB(1, 1, 1, 1, 1);

            public ResourceManagerWorkItemTracker(LoggingContext loggingContext, ProcessResourceManager resourceManager, uint id, int estimatedRamUsage, bool allowCancellation)
            {
                ExecutionCompletionSource = TaskSourceSlim.Create<int>();
                m_cancellationCompletionSource = TaskSourceSlim.Create<Unit>();
                var context = BuildXLContext.CreateInstanceForTesting();
                m_execute = () => ExecutionTask = resourceManager.ExecuteWithResourcesAsync(
                    OperationContext.CreateUntracked(loggingContext),
                    SchedulerTest.CreateDummyProcess(context, new PipId(id)),
                    ProcessMemoryCounters.CreateFromMb(estimatedRamUsage, estimatedRamUsage, estimatedRamUsage, estimatedRamUsage),
                    allowCancellation,
                    async (resourceScope) =>
                {
                    m_startSemaphore.Release();
                    resourceScope.RegisterQueryRamUsageMb(() => RamUsage);

                    ExecutionCount++;
                    var currrentCancellationCompletionSource = m_cancellationCompletionSource;
                    resourceScope.Token.Register(() =>
                    {
                        XAssert.IsTrue(m_startSemaphore.Wait(millisecondsTimeout: 100000));

                        CancellationCount++;
                        m_cancellationCompletionSource = TaskSourceSlim.Create<Unit>();
                        currrentCancellationCompletionSource.TrySetResult(Unit.Void);
                    });

                    var result = await Task.WhenAny(currrentCancellationCompletionSource.Task, ExecutionCompletionSource.Task);

                    resourceScope.Token.ThrowIfCancellationRequested();

                    XAssert.IsTrue(ExecutionCompletionSource.Task.IsCompleted, "This task should be completed since the cancellation task implies the cancellation token would throw in the preceding line of code.");

                    return ExecutionCompletionSource.Task.Result;
                });

                StartExecute();
            }

            public void StartExecute()
            {
                Analysis.IgnoreResult(
                    m_execute(),
                    justification: "Fire and Forget"
                );
            }

            public async Task WaitAndVerifyRestarted(int timeoutMs = 100000)
            {
                var result = await m_startSemaphore.WaitAsync(millisecondsTimeout: timeoutMs);
                XAssert.IsTrue(result);

                // Immediately release. We just needed to verify that the semaphore could be acquired
                m_startSemaphore.Release();
            }

            public async Task<bool> WaitForCancellation(int timeoutMs = 100000)
            {
                var cancellationCompletionSource = m_cancellationCompletionSource;
                Analysis.IgnoreResult(
                    await Task.WhenAny(Task.Delay(timeoutMs), cancellationCompletionSource.Task)
                );

                return cancellationCompletionSource.Task.IsCompleted;
            }

            public void Verify(int? expectedCancellationCount = null, int? expectedExecutionCount = null, bool? expectedCompleted = null)
            {
                if (expectedCancellationCount != null)
                {
                    XAssert.AreEqual(expectedCancellationCount.Value, CancellationCount);
                }

                if (expectedExecutionCount != null)
                {
                    XAssert.AreEqual(expectedExecutionCount.Value, ExecutionCount);
                }

                if (expectedCompleted != null)
                {
                    XAssert.AreEqual(expectedCompleted.Value, ExecutionTask.Status == TaskStatus.RanToCompletion);
                }
            }
        }
    }
}
