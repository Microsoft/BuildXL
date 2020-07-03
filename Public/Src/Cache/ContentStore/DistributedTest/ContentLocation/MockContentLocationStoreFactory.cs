// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Distributed.Redis.Credentials;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using ContentStoreTest.Distributed.Redis;
using ContentStoreTest.Distributed.Stores;

namespace ContentStoreTest.Distributed.ContentLocation
{
    public sealed class MockContentLocationStoreFactory : ContentLocationStoreFactory
    {
        private readonly ITestRedisDatabase _primaryRedisDatabase;
        private readonly ITestRedisDatabase _secondaryRedisDatabase;

        public TestDistributedContentCopier GetCopier() => (TestDistributedContentCopier)Copier;

        public MockContentLocationStoreFactory(
            ITestRedisDatabase primaryRedisDatabase,
            ITestRedisDatabase secondaryRedisDatabase,
            AbsolutePath rootDirectory,
            ITestClock mockClock = null,
            RedisContentLocationStoreConfiguration configuration = null)
        : base(mockClock ?? TestSystemClock.Instance, configuration ?? CreateDefaultConfiguration(rootDirectory), CreateTestCopier(mockClock ?? TestSystemClock.Instance, rootDirectory))
        {
            _primaryRedisDatabase = primaryRedisDatabase;
            _secondaryRedisDatabase = secondaryRedisDatabase;
        }

        /// <inheritdoc />
        protected override void DisposeCore()
        {
            base.DisposeCore();

            _primaryRedisDatabase?.Dispose();
            _secondaryRedisDatabase?.Dispose();
        }

        private static RedisContentLocationStoreConfiguration CreateDefaultConfiguration(AbsolutePath rootDirectory)
        {
            var configuration = new RedisContentLocationStoreConfiguration()
                   {
                       Keyspace = "Default:",
                       RedisGlobalStoreConnectionString = "Test",
                   };

            configuration.InlinePostInitialization = true;
            configuration.MachineStateRecomputeInterval = TimeSpan.Zero;
            configuration.EventStore = new MemoryContentLocationEventStoreConfiguration();
            configuration.Database = new RocksDbContentLocationDatabaseConfiguration(rootDirectory / "rocksdb");
            configuration.Checkpoint = new CheckpointConfiguration(rootDirectory);
            configuration.CentralStore = new LocalDiskCentralStoreConfiguration(rootDirectory / "centralStore", "checkpoints-key");
            configuration.PrimaryMachineLocation = new MachineLocation(rootDirectory.ToString());
            return configuration;
        }

        /// <inheritdoc />
        protected override IGlobalLocationStore CreateRedisGlobalStore()
        {
            var primaryConnection = MockRedisDatabaseFactory.CreateConnection(_primaryRedisDatabase);
            var primaryDatabaseAdapter = new RedisDatabaseAdapter(
                RedisDatabaseFactory.CreateAsync(new EnvironmentConnectionStringProvider("TestConnectionString"), primaryConnection).GetAwaiter().GetResult(),
                DefaultKeySpace);
            var secondaryConnection = MockRedisDatabaseFactory.CreateConnection(_secondaryRedisDatabase);
            var secondaryDatabaseAdapter = new RedisDatabaseAdapter(
                RedisDatabaseFactory.CreateAsync(new EnvironmentConnectionStringProvider("TestConnectionString"), secondaryConnection).GetAwaiter().GetResult(),
                DefaultKeySpace);

            return new RedisGlobalStore(Clock, Configuration, primaryDatabaseAdapter, secondaryDatabaseAdapter);
        }

        //private static TestDistributedContentCopier CreateTestCopier()
        private static TestDistributedContentCopier CreateTestCopier(ITestClock clock, AbsolutePath rootDirectory)
        {
            var (copier, _) = DistributedContentCopierTests.CreateMocks(
                new MemoryFileSystem(clock),
                rootDirectory,
                TimeSpan.FromSeconds(1));
            return copier;

        }
    }
}
