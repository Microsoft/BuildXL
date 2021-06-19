// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using ProtoBuf;

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService
{
    public enum RpcMethodId
    {
        GetContentLocations = 0,
        RegisterContentLocations = 1,
        PutBlob = 2,
        GetBlob = 3,
        CompareExchange = 4,
        GetLevelSelectors = 5,
        GetContentHashList = 6,
    }

    public static class ServiceRequestExtensions
    {
        public static TResult ToResult<TResponse, TResult>(this TResponse response, Func<TResponse, TResult> select)
            where TResponse : ServiceResponseBase
            where TResult : ResultBase
        {
            if (response.Succeeded)
            {
                return select(response);
            }
            else
            {
                return new ErrorResult(response.ErrorMessage, response.Diagnostics).AsResult<TResult>();
            }
        }
    }

    [ProtoContract]
    [ProtoInclude(10, typeof(GetContentLocationsRequest))]
    [ProtoInclude(11, typeof(RegisterContentLocationsRequest))]
    [ProtoInclude(12, typeof(PutBlobRequest))]
    [ProtoInclude(13, typeof(GetBlobRequest))]
    [ProtoInclude(14, typeof(GetContentHashListRequest))]
    [ProtoInclude(15, typeof(CompareExchangeRequest))]
    [ProtoInclude(16, typeof(GetLevelSelectorsRequest))]
    public abstract record ServiceRequestBase
    {
        public abstract RpcMethodId MethodId { get; }

        [ProtoMember(1)]
        public string ContextId { get; set; }

        public bool Replaying { get; set; }

        public BlockReference? BlockId { get; set; }
    }

    [ProtoContract]
    [ProtoInclude(10, typeof(GetContentLocationsResponse))]
    [ProtoInclude(11, typeof(RegisterContentLocationsResponse))]
    [ProtoInclude(12, typeof(PutBlobResponse))]
    [ProtoInclude(13, typeof(GetBlobResponse))]
    [ProtoInclude(14, typeof(GetContentHashListResponse))]
    [ProtoInclude(15, typeof(CompareExchangeResponse))]
    [ProtoInclude(16, typeof(GetLevelSelectorsResponse))]
    public abstract record ServiceResponseBase
    {
        public abstract RpcMethodId MethodId { get; }

        public bool Succeeded
        {
            get
            {
                if (ShouldRetry)
                {
                    throw new ClientCanRetryException("Target machine indicated that retry is needed.");
                }

                return ErrorMessage == null;
            }
        }

        [ProtoMember(1)]
        public string ErrorMessage { get; set; }

        [ProtoMember(2)]
        public string Diagnostics { get; set; }

        [ProtoMember(3)]
        public bool ShouldRetry { get; set; }

        public bool PersistRequest { get; init; }
    }

    [ProtoContract]
    public record GetContentLocationsRequest : ServiceRequestBase
    {
        public override RpcMethodId MethodId => RpcMethodId.GetContentLocations;

        [ProtoMember(1)]
        public IReadOnlyList<ShortHash> Hashes { get; init; } = new List<ShortHash>();
    }

    [ProtoContract]
    public record GetContentLocationsResponse : ServiceResponseBase
    {
        public override RpcMethodId MethodId => RpcMethodId.GetContentLocations;

        [ProtoMember(1)]
        public IReadOnlyList<ContentLocationEntry> Entries { get; init; } = new List<ContentLocationEntry>();
    }

    [ProtoContract]
    public record RegisterContentLocationsRequest : ServiceRequestBase
    {
        public override RpcMethodId MethodId => RpcMethodId.RegisterContentLocations;

        [ProtoMember(1)]
        public IReadOnlyList<ShortHashWithSize> Hashes { get; init; } = new List<ShortHashWithSize>();

        [ProtoMember(2)]
        public MachineId MachineId { get; init; }
    }

    [ProtoContract]
    public record RegisterContentLocationsResponse : ServiceResponseBase
    {
        public override RpcMethodId MethodId => RpcMethodId.RegisterContentLocations;
    }

    [ProtoContract]
    public record PutBlobRequest : ServiceRequestBase
    {
        public override RpcMethodId MethodId => RpcMethodId.PutBlob;

        [ProtoMember(1)]
        public ShortHash ContentHash { get; init; }

        [ProtoMember(2)]
        public byte[] Blob { get; init; }
    }

    [ProtoContract]
    public record PutBlobResponse : ServiceResponseBase
    {
        public override RpcMethodId MethodId => RpcMethodId.PutBlob;

        public PutBlobResult ToPutBlobResult(ShortHash contentHash, long blobSize)
        {
            if (ErrorMessage != null)
            {
                return new PutBlobResult(hash: contentHash, blobSize: blobSize, errorMessage: ErrorMessage);
            }
            else
            {
                return new PutBlobResult(hash: contentHash, blobSize: blobSize);
            }
        }
    }

    [ProtoContract]
    public record GetBlobRequest : ServiceRequestBase
    {
        public override RpcMethodId MethodId => RpcMethodId.GetBlob;

        [ProtoMember(1)]
        public ShortHash ContentHash { get; init; }
    }

    [ProtoContract]
    public record GetBlobResponse : ServiceResponseBase
    {
        public override RpcMethodId MethodId => RpcMethodId.GetBlob;

        [ProtoMember(1)]
        public byte[] Blob { get; init; }

        internal GetBlobResult ToGetBlobResult(ShortHash contentHash)
        {
            if (ErrorMessage != null)
            {
                return new GetBlobResult(ErrorMessage, Diagnostics, contentHash);
            }
            else
            {
                return new GetBlobResult(contentHash, Blob);
            }
        }
    }

    [ProtoContract]
    public record CompareExchangeRequest : ServiceRequestBase
    {
        public override RpcMethodId MethodId => RpcMethodId.CompareExchange;

        [ProtoMember(1)]
        public StrongFingerprint StrongFingerprint { get; init; }

        [ProtoMember(2)]
        public SerializedMetadataEntry Replacement { get; init; }

        [ProtoMember(3)]
        public string ExpectedReplacementToken { get; init; }
    }

    [ProtoContract]
    public record CompareExchangeResponse : ServiceResponseBase
    {
        public override RpcMethodId MethodId => RpcMethodId.CompareExchange;

        [ProtoMember(1)]
        public bool Exchanged { get; init; }
    }

    [ProtoContract]
    public record GetLevelSelectorsRequest : ServiceRequestBase
    {
        public override RpcMethodId MethodId => RpcMethodId.GetLevelSelectors;

        [ProtoMember(1)]
        public Fingerprint WeakFingerprint { get; init; }

        [ProtoMember(2)]
        public int Level { get; init; }
    }

    [ProtoContract]
    public record GetLevelSelectorsResponse : ServiceResponseBase
    {
        public override RpcMethodId MethodId => RpcMethodId.GetLevelSelectors;

        [ProtoMember(1)]
        public IReadOnlyList<Selector> Selectors { get; init; } = new List<Selector>();

        [ProtoMember(2)]
        public bool HasMore { get; init; }
    }

    [ProtoContract]
    public record GetContentHashListRequest : ServiceRequestBase
    {
        public override RpcMethodId MethodId => RpcMethodId.GetContentHashList;

        [ProtoMember(1)]
        public StrongFingerprint StrongFingerprint { get; init; }
    }

    [ProtoContract]
    public record GetContentHashListResponse : ServiceResponseBase
    {
        public override RpcMethodId MethodId => RpcMethodId.GetContentHashList;

        [ProtoMember(1)]
        public SerializedMetadataEntry MetadataEntry { get; init; }
    }

    [ProtoContract]
    public class SerializedMetadataEntry
    {
        [ProtoMember(1)]
        public byte[] Data { get; init;  }

        [ProtoMember(2)]
        public string ReplacementToken { get; set; }

        [ProtoMember(3)]
        public long? SequenceNumber { get; set; }
    }
}
