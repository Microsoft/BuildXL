// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using BuildXL.ContentStore.Exceptions;
using BuildXL.ContentStore.FileSystem;
using BuildXL.ContentStore.Logging;
using BuildXL.ContentStore.Stores;
using BuildXL.ContentStore.Synchronization;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

namespace SampleApp
{
    static class Program
    {
        // ReSharper disable once UnusedParameter.Local
        static int Main(string[] args)
        {
            int resultCode;

            using (var log = new ConsoleLog())
            using (var logger = new Logger(log))
            {
                try
                {
                    using (var fileSystem = new PassThroughFileSystem())
                    using (var directory = new DisposableDirectory(fileSystem))
                    using (var store = new FileSystemContentStore(fileSystem, logger, SystemClock.Instance, directory.Path))
                    {
                        var context = new Context(logger);

                        // ReSharper disable once AccessToDisposedClosure
                        resultCode = TaskSafetyHelpers.SyncResultOnThreadPool(() => RunStore(context, store));
                    }
                }
                catch (Exception exception)
                {
                    logger.Error($"Unexpected error: {exception}");
                    resultCode = 1;
                }
            }

            return resultCode;
        }

        static async Task<int> RunStore(Context context, IContentStore store)
        {
            try
            {
                try
                {
                    StartupResult startupResult = await store.StartupAsync(context).ConfigureAwait(false);
                    if (startupResult.HasError)
                    {
                        throw new CacheException($"Failed to start store, error=[{startupResult.ErrorMessage}]");
                    }

                    var createSessionResult = store.CreateSession(context, "sample", ImplicitPin.None);
                    if (createSessionResult.HasError)
                    {
                        throw new CacheException($"Failed to create session, error=[{createSessionResult.ErrorMessage}]");
                    }

                    using (var session = createSessionResult.Session)
                    {
                        try
                        {
                            var sessionStartupResult = session.StartupAsync(context).Result;
                            if (sessionStartupResult.HasError)
                            {
                                throw new CacheException($"Failed to start session, error=[{createSessionResult.ErrorMessage}]");
                            }
                        }
                        finally
                        {
                            var sessionShutdownResult = session.ShutdownAsync(context).Result;
                            if (sessionShutdownResult.HasError)
                            {
                                context.Error($"Failed to shutdown session, error=[{sessionShutdownResult.ErrorMessage}]");
                            }
                        }
                    }
                }
                finally
                {
                    ShutdownResult shutdownResult = await store.ShutdownAsync(context).ConfigureAwait(false);
                    if (shutdownResult.HasError)
                    {
                        context.Error($"Failed to shutdown store, error=[{shutdownResult.ErrorMessage}]");
                    }
                }

                return 0;
            }
            catch (Exception)
            {
                return 1;
            }
        }
    }
}
