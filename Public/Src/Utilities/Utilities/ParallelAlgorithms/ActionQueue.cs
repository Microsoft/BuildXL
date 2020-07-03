// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Utilities.ParallelAlgorithms
{
    /// <summary>
    /// Wrapper of <see cref="ActionBlockSlim{T}"/> allowing running generic synchronous and asynchronous actions with limited
    /// parallelism and awaiting the results individually.
    /// </summary>
    public sealed class ActionQueue
    {
        private readonly ActionBlockSlim<Func<Task>> m_actionBlock;

        /// <nodoc />
        public ActionQueue(int degreeOfParallelism)
        {
            m_actionBlock = ActionBlockSlim<Func<Task>>.CreateWithAsyncAction(degreeOfParallelism, f => f());
        }

        /// <summary>
        /// Runs the delegate asynchronously for all items and returns the completion
        /// </summary>
        public Task ForEachAsync<T>(IEnumerable<T> items, Func<T, int, Task> body)
        {
            var tasks = new List<Task>();

            int index = 0;
            foreach (var item in items)
            {
                var itemIndex = index;
                tasks.Add(RunAsync(() =>
                {
                    return body(item, itemIndex);
                }));
                index++;
            }

            return Task.WhenAll(tasks);
        }

        /// <summary>
        /// Runs the delegate asynchronously and returns the completion
        /// </summary>
        public Task<T> RunAsync<T>(Func<T> func)
        {
            return RunAsync(() =>
            {
                var result = func();
                return Task.FromResult<T>(result);
            });
        }

        /// <summary>
        /// Runs the delegate asynchronously and returns the completion
        /// </summary>
        public Task RunAsync(Action action)
        {
            return RunAsync(() =>
            {
                action();
                return Task.CompletedTask;
            });
        }

        /// <summary>
        /// Runs the delegate asynchronously and returns the completion
        /// </summary>
        public Task RunAsync(Func<Task> runAsync)
        {
            return RunAsync(async () =>
            {
                await runAsync();
                return Unit.Void;
            });
        }

        /// <summary>
        /// Runs the delegate asynchronously and returns the completion
        /// </summary>
        public Task<T> RunAsync<T>(Func<Task<T>> runAsync)
        {
            var taskSource = TaskSourceSlim.Create<T>();

            m_actionBlock.Post(() =>
            {
                try
                {
                    var task = runAsync();
                    taskSource.LinkToTask(task);
                    return task;
                }
                catch (Exception ex)
                {
                    taskSource.TrySetException(ex);
                    return Task.CompletedTask;
                }
            });

            return taskSource.Task;
        }
    }
}
