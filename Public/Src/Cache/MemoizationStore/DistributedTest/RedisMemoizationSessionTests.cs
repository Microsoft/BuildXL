﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using ContentStoreTest.Test;
using BuildXL.Cache.MemoizationStore.Interfaces.Stores;
using BuildXL.Cache.MemoizationStore.InterfacesTest.Sessions;
using System;
using ContentStoreTest.Distributed.Redis;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.MemoizationStore.Distributed.Stores;
using Xunit;
using System.Threading.Tasks;

namespace BuildXL.Cache.MemoizationStore.Test.Sessions
{
    [Trait("Category", "LongRunningTest")]
    [Collection("Redis-based tests")]
    public class RedisMemoizationSessionTests : MemoizationSessionTests
    {
        private readonly MemoryClock _clock = new MemoryClock();
        private readonly LocalRedisFixture _redis;
        private readonly ILogger _logger;

        public RedisMemoizationSessionTests(LocalRedisFixture redis)
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), TestGlobal.Logger)
        {
            _redis = redis;
            _logger = TestGlobal.Logger;
        }

        protected override IMemoizationStore CreateStore(DisposableDirectory testDirectory)
        {
            var context = new Context(_logger);
            var localDatabase = LocalRedisProcessDatabase.CreateAndStartEmpty(_redis, _logger, _clock);
            var connectionString = localDatabase.ConnectionString;

            var connectionStringProvider = new LiteralConnectionStringProvider(connectionString);
            var redisFactory = RedisDatabaseFactory.CreateAsync(context, connectionStringProvider).GetAwaiter().GetResult();
            var redisAdapter = new RedisDatabaseAdapter(redisFactory, keySpace: Guid.NewGuid().ToString());

            var memoizationDb = new RedisMemoizationDatabase(redisAdapter, _clock);
            return new RedisMemoizationStore(_logger, memoizationDb);
        }

        public override Task EnumerateStrongFingerprints(int strongFingerprintCount)
        {
            // Do nothing, since operation isn't supported in Redis.
            return Task.FromResult(0);
        }
        public override Task EnumerateStrongFingerprintsEmpty()
        {
            // Do nothing, since operation isn't supported in Redis.
            return Task.FromResult(0);
        }
    }

    /// <summary>
    /// Custom collection that uses <see cref="LocalRedisFixture"/>.
    /// </summary>
    [CollectionDefinition("Redis-based tests")]
    public class LocalRedisCollection : ICollectionFixture<LocalRedisFixture>
    {
        // This class has no code, and is never created. Its purpose is simply
        // to be the place to apply [CollectionDefinition] and all the
        // ICollectionFixture<> interfaces.
    }
}
