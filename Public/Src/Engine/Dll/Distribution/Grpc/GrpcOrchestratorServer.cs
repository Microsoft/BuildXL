// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using BuildXL.Distribution.Grpc;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace BuildXL.Engine.Distribution.Grpc
{
    /// <summary>
    /// Orchestrator service impl
    /// </summary>
    public sealed class GrpcOrchestratorServer : Orchestrator.OrchestratorBase, IServer
    {
        private readonly IOrchestratorService m_orchestratorService;
        private readonly LoggingContext m_loggingContext;
        private readonly DistributedInvocationId m_invocationId;

        private Server m_server;
       
        // Expose the port to unit tests
        internal int? Port => m_server?.Ports.FirstOrDefault()?.BoundPort;

        /// <summary>
        /// Class constructor
        /// </summary>
        public GrpcOrchestratorServer(LoggingContext loggingContext, OrchestratorService orchestratorService, DistributedInvocationId invocationId) : this(loggingContext, (IOrchestratorService)orchestratorService, invocationId)
        {
        }

        internal GrpcOrchestratorServer(LoggingContext loggingContext, IOrchestratorService orchestratorService, DistributedInvocationId invocationId)
        {
            m_loggingContext = loggingContext;
            m_orchestratorService = orchestratorService;
            m_invocationId = invocationId;
        }

        /// <nodoc/>
        public void Start(int port)
        {
            var interceptor = new ServerInterceptor(m_loggingContext, m_invocationId);
            m_server = new Server(ClientConnectionManager.ServerChannelOptions)
            {
                Services = { Orchestrator.BindService(this).Intercept(interceptor) },
                Ports = { new ServerPort(IPAddress.Any.ToString(), port, ServerCredentials.Insecure) },
            };
            m_server.Start();
        }

        /// <nodoc/>
        public async Task ShutdownAsync()
        {
            if (m_server != null)
            {
                try
                {
                    await m_server.ShutdownAsync();
                }
                catch (InvalidOperationException)
                {
                    // Shutdown was already requested
                }
            }
        }

        /// <inheritdoc />
        public void Dispose() => DisposeAsync().GetAwaiter().GetResult();

        /// <inheritdoc />
        public Task DisposeAsync() => ShutdownAsync();

        #region Service Methods

        /// <inheritdoc/>
        public override Task<RpcResponse> AttachCompleted(AttachCompletionInfo message, ServerCallContext context)
        {
            var bondMessage = message.ToOpenBond();

            m_orchestratorService.AttachCompleted(bondMessage);

            return Task.FromResult(new RpcResponse());
        }

        /// <inheritdoc/>
        public override async Task<RpcResponse> Notify(WorkerNotificationArgs message, ServerCallContext context)
        {
            var bondMessage = message.ToOpenBond();

            var notifyTask = m_orchestratorService.ReceivedWorkerNotificationAsync(bondMessage);
            if (EngineEnvironmentSettings.InlineWorkerXLGHandling)
            {
                await notifyTask;
            }
            else
            {
                notifyTask.Forget();
            }

            return new RpcResponse();
        }

        #endregion Service Methods
    }
}