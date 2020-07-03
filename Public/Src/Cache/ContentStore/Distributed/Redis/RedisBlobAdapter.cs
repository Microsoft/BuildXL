﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Utilities.Tracing;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Redis
{
    /// <summary>
    /// Class in charge of putting and getting blobs to/from Redis.
    /// There is a limit to how many bytes it can put into Redis, which is enforced through a reservation strategy. 
    /// </summary>
    internal sealed class RedisBlobAdapter
    {
        public enum RedisBlobAdapterCounters
        {
            SkippedBlobs,
            FailedReservations,
            DownloadedBytes,
            DownloadedBlobs
        }

        private readonly RedisDatabaseAdapter _redis;
        private readonly TimeSpan _blobExpiryTime;
        private readonly TimeSpan _capacityExpiryTime;
        private string? _lastFailedReservationKey;
        private readonly long _maxCapacityPerTimeBox;
        private readonly IClock _clock;

        internal CounterCollection<RedisBlobAdapterCounters> Counters { get; } = new CounterCollection<RedisBlobAdapterCounters>();

        internal static string GetBlobKey(ContentHash hash) => $"Blob-{hash}";

        public RedisBlobAdapter(RedisDatabaseAdapter redis, TimeSpan blobExpiryTime, long maxCapacity, IClock clock)
        {
            _redis = redis;
            _blobExpiryTime = blobExpiryTime;
            _capacityExpiryTime = blobExpiryTime.Add(TimeSpan.FromMinutes(5));
            _maxCapacityPerTimeBox = maxCapacity / 2;
            _clock = clock;
        }

        /// <summary>
        ///     Puts a blob into Redis. Will fail only if capacity cannot be reserved or if Redis fails in some way.
        /// </summary>
        public async Task<PutBlobResult> PutBlobAsync(OperationContext context, ContentHash hash, byte[] blob)
        {
            const string errorMessage = "Redis value could not be updated to upload blob.";
            try
            {
                var key = GetBlobKey(hash);

                if (await _redis.KeyExistsAsync(context, key, context.Token))
                {

                    Counters[RedisBlobAdapterCounters.SkippedBlobs].Increment();
                    return new PutBlobResult(hash, blob.Length, alreadyInRedis: true);
                }

                var reservationResult = await TryReserveAsync(context, blob.Length, hash);
                if (!reservationResult)
                {
                    Counters[RedisBlobAdapterCounters.FailedReservations].Increment();
                    return new PutBlobResult(hash, blob.Length, reservationResult.ErrorMessage!);
                }

                var success = await _redis.StringSetAsync(context, key, blob, _blobExpiryTime, StackExchange.Redis.When.Always, context.Token);

                return success
                    ? new PutBlobResult(hash, blob.Length, alreadyInRedis: false, newCapacity: reservationResult.Value.newCapacity, redisKey: reservationResult.Value.key)
                    : new PutBlobResult(hash, blob.Length, errorMessage);
            }
            catch (Exception e)
            {
                return new PutBlobResult(new ErrorResult(e), errorMessage, hash, blob.Length);
            }
        }

        /// <summary>
        ///     The reservation strategy consists of timeboxes of 30 minutes, where each box only has half the max permitted
        /// capacity. This is to account for Redis not deleting files exactly when their TTL expires.
        ///     Under this scheme, each blob will try to add its length to its box's capacity and fail if max capacity has
        /// been exceeded.
        /// </summary>
        private async Task<Result<(long newCapacity, string key)>> TryReserveAsync(OperationContext context, long byteCount, ContentHash hash)
        {
            var operationStart = _clock.UtcNow;
            var time = new DateTime(ticks: operationStart.Ticks / _blobExpiryTime.Ticks * _blobExpiryTime.Ticks);
            var key = $"BlobCapacity@{time.ToString("yyyyMMdd:hhmmss.fff")}";

            if (key == _lastFailedReservationKey)
            {
                string message = $"Skipping reservation for blob [{hash.ToShortString()}] because key [{key}] ran out of capacity.";
                return Result.FromErrorMessage<(long newCapacity, string key)>(message);
            }
            
            var newUsedCapacity = await _redis.ExecuteBatchAsync(context, async batch =>
            {
                var stringSetTask = batch.StringSetAsync(key, 0, _capacityExpiryTime, StackExchange.Redis.When.NotExists);
                var incrementTask = batch.StringIncrementAsync(key, byValue: byteCount);

                await Task.WhenAll(stringSetTask, incrementTask);
                return await incrementTask;
            }, RedisOperation.StringIncrement);

            var couldReserve = newUsedCapacity <= _maxCapacityPerTimeBox;

            if (!couldReserve)
            {
                _lastFailedReservationKey = key;
                string error = $"Could not reserve {byteCount} for {hash.ToShortString()} because key [{key}] ran out of capacity. Expected new capacity={newUsedCapacity} bytes, Max capacity={_maxCapacityPerTimeBox} bytes.";
                return Result.FromErrorMessage<(long newCapacity, string key)>(error);
            }

            return Result.Success((newUsedCapacity, key));
        }

        /// <summary>
        ///     Tries to get a blob from Redis.
        /// </summary>
        public async Task<GetBlobResult> GetBlobAsync(OperationContext context, ContentHash hash)
        {
            try
            {
                byte[] result = await _redis.StringGetAsync(context, GetBlobKey(hash), context.Token);

                if (result == null)
                {
                    return new GetBlobResult(hash, blob: null);
                }

                Counters[RedisBlobAdapterCounters.DownloadedBytes].Add(result.Length);
                Counters[RedisBlobAdapterCounters.DownloadedBlobs].Increment();
                return new GetBlobResult(hash, result);
            }
            catch (Exception e)
            {
                return new GetBlobResult(new ErrorResult(e), "Blob could not be fetched from redis.", hash);
            }
        }

        public CounterSet GetCounters() => Counters.ToCounterSet();
    }
}
