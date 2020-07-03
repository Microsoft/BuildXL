// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using Grpc.Core;

namespace BuildXL.Cache.ContentStore.Service.Grpc
{
    /// <summary>
    /// Environment initialization for GRPC
    /// </summary>
    /// <remarks>
    /// GRPC is special and needs to initialize the environment
    /// before it is started. Also needs to be initialized only
    /// once.
    /// </remarks>
    public static class GrpcEnvironment
    {
        /// <summary>
        /// The local host
        /// </summary>
        public const string Localhost = "localhost";

        private static bool _isInitialized;

        private static readonly object _initializationLock = new object();

        /// <summary>
        /// Allow sent and received message to have (essentially) unbounded length. This does not cause GRPC to send larger packets, but it does allow larger packets to exist.
        /// </summary>
        public static readonly List<ChannelOption> DefaultConfiguration = new List<ChannelOption>() { new ChannelOption(ChannelOptions.MaxSendMessageLength, int.MaxValue), new ChannelOption(ChannelOptions.MaxReceiveMessageLength, int.MaxValue) };

        /// <summary>
        /// Initialize the GRPC environment if not yet initialized.
        /// </summary>
        public static void InitializeIfNeeded(int numThreads = 70, bool handlerInliningEnabled = true)
        {
            // Using double-checked locking to avoid race condition.
            // The thread that looses the race must wait for the initialization to finish,
            // otherwise the thread may start the channel creation that violate the invariants of GrpcEnvironment 
            // and the initialization that is happening in parallel in another thread may fail.
            if (!_isInitialized)
            {
                lock (_initializationLock)
                {
                    if (!_isInitialized)
                    {
                        // Setting GRPC_DNS_RESOLVER=native to bypass ares DNS resolver which seems to cause
                        // temporary long delays (2 minutes) while failing to resolve DNS using ares in some environments
                        Environment.SetEnvironmentVariable("GRPC_DNS_RESOLVER", "native");

                        if (handlerInliningEnabled)
                        {
                            global::Grpc.Core.GrpcEnvironment.SetThreadPoolSize(numThreads);
                            global::Grpc.Core.GrpcEnvironment.SetCompletionQueueCount(numThreads);
                        }

                        // By default, gRPC's internal event handlers get offloaded to .NET default thread pool thread (inlineHandlers=false).
                        // Setting inlineHandlers to true will allow scheduling the event handlers directly to GrpcThreadPool internal threads.
                        // That can lead to significant performance gains in some situations, but requires user to never block in async code
                        // (incorrectly written code can easily lead to deadlocks). Inlining handlers is an advanced setting and you should
                        // only use it if you know what you are doing. Most users should rely on the default value provided by gRPC library.
                        // Note: this method is part of an experimental API that can change or be removed without any prior notice.
                        // Note: inlineHandlers=true was the default in gRPC C# v1.4.x and earlier.
                        global::Grpc.Core.GrpcEnvironment.SetHandlerInlining(handlerInliningEnabled);

                        _isInitialized = true;
                    }
                }
            }
        }
    }
}