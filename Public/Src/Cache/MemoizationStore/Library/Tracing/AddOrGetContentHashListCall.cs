// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

namespace BuildXL.Cache.MemoizationStore.Tracing
{
    /// <summary>
    ///     Instance of a AddOrGetContentHashList operation for tracing purposes.
    /// </summary>
    public sealed class AddOrGetContentHashListCall : TracedCall<MemoizationStoreTracer, AddOrGetContentHashListResult>, IDisposable
    {
        private readonly StrongFingerprint _fingerprint;

        /// <summary>
        ///     Run the call.
        /// </summary>
        public static async Task<AddOrGetContentHashListResult> RunAsync(
            MemoizationStoreTracer tracer, OperationContext context, StrongFingerprint fingerprint, Func<Task<AddOrGetContentHashListResult>> funcAsync)
        {
            using (var call = new AddOrGetContentHashListCall(tracer, context, fingerprint))
            {
                return await call.RunAsync(funcAsync).ConfigureAwait(false);
            }
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="AddOrGetContentHashListCall"/> class.
        /// </summary>
        private AddOrGetContentHashListCall(MemoizationStoreTracer tracer, OperationContext context, StrongFingerprint fingerprint)
            : base(tracer, context)
        {
            _fingerprint = fingerprint;
            Tracer.AddOrGetContentHashListStart(Context, _fingerprint);
        }

        /// <inheritdoc />
        protected override AddOrGetContentHashListResult CreateErrorResult(Exception exception)
        {
            return new AddOrGetContentHashListResult(exception);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Tracer.AddOrGetContentHashListStop(Context, Result, _fingerprint);
        }
    }
}
