﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using ContentStoreTest.Distributed.ContentLocation;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Distributed.Stores
{
    public class DistributedContentCopierTests : TestBase
    {
        public DistributedContentCopierTests()
            : base(() => new MemoryFileSystem(TestSystemClock.Instance), TestGlobal.Logger)
        {
        }

        [Fact]
        public async Task CopyFailsForWrongCopySize()
        {
            var context = new Context(Logger);
            using (var directory = new DisposableDirectory(FileSystem))
            {
                var(distributedCopier, mockFileCopier) = CreateMocks(FileSystem, directory.Path, TimeSpan.Zero);

                var hash = VsoHashInfo.Instance.EmptyHash;
                var hashWithLocations = new ContentHashWithSizeAndLocations(
                    hash,
                    size: 42,
                    new MachineLocation[] {new MachineLocation("")});

                mockFileCopier.CopyToAsyncResult = CopyFileResult.SuccessWithSize(41);
                var result = await distributedCopier.TryCopyAndPutAsync(
                    new OperationContext(context),
                    hashWithLocations,
                    handleCopyAsync: tpl => Task.FromResult(new PutResult(hash, 42))
                );

                result.ShouldBeError();
                result.ErrorMessage.Should().Contain("size");
                result.ErrorMessage.Should().Contain("mismatch");
            }
        }

        [Fact]
        public async Task CopyFailsForWrongHash()
        {
            var context = new Context(Logger);
            using (var directory = new DisposableDirectory(FileSystem))
            {
                var(distributedCopier, mockFileCopier) = CreateMocks(FileSystem, directory.Path, TimeSpan.Zero);

                var hash = ContentHash.Random();
                var wrongHash = VsoHashInfo.Instance.EmptyHash;
                var hashWithLocations = new ContentHashWithSizeAndLocations(
                    hash,
                    size: 42,
                    new MachineLocation[] {new MachineLocation("")});

                mockFileCopier.CopyToAsyncResult = CopyFileResult.SuccessWithSize(42);
                var result = await distributedCopier.TryCopyAndPutAsync(
                    new OperationContext(context),
                    hashWithLocations,
                    handleCopyAsync: tpl => Task.FromResult(new PutResult(wrongHash, 42))
                );

                result.ShouldBeError();
                result.ErrorMessage.Should().Contain(hash.ToShortString());
                result.ErrorMessage.Should().Contain(wrongHash.ToShortString());
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(10)]
        public async Task CopyRetries(int retries)
        {
            var context = new Context(Logger);
            using (var directory = new DisposableDirectory(FileSystem))
            {
                var (distributedCopier, mockFileCopier) = CreateMocks(FileSystem, directory.Path,TimeSpan.Zero, retries);
                var machineLocations = new MachineLocation[] {new MachineLocation("")};

                var hash = ContentHash.Random();
                var hashWithLocations = new ContentHashWithSizeAndLocations(
                    hash,
                    size: 99,
                    machineLocations);

                mockFileCopier.CopyToAsyncResult = new CopyFileResult(CopyFileResult.ResultCode.SourcePathError);
                var result = await distributedCopier.TryCopyAndPutAsync(
                    new OperationContext(context),
                    hashWithLocations,
                    handleCopyAsync: tpl => Task.FromResult(new PutResult(hash, 99)));

                result.ShouldBeError();
                mockFileCopier.CopyAttempts.Should().Be(retries);
            }
        }

        ///<summary>
        /// Test case for bug https://dev.azure.com/mseng/1ES/_boards/board/t/DavidW%20-%20Team/Stories/?workitem=1654106
        /// During the first attempt of copying from a list of locations, one of the locations returns a DestinationPathError.
        /// Then in subsequent attempts to copy from the list of locations, the previous location that returned DestinationPathError now returns a different error.
        /// We should still be able to attempt to copy and return without and out of range exception thrown.
        ///</summary>
        [Theory]
        [InlineData(3)]
        public async Task CopyWithDestinationPathError(int retries)
        {
            var context = new Context(Logger);
            using (var directory = new DisposableDirectory(FileSystem))
            {
                var (distributedCopier, mockFileCopier) = CreateMocks(FileSystem, directory.Path, TimeSpan.FromMilliseconds((10)), retries);
                var machineLocations = new MachineLocation[] { new MachineLocation(""), new MachineLocation("") };

                var hash = ContentHash.Random();
                var hashWithLocations = new ContentHashWithSizeAndLocations(
                    hash,
                    size: 99,
                    machineLocations);
                mockFileCopier.CopyAttempts = 0;
                var totalCopyAttempts = (retries - 1) * machineLocations.Length + 1;
                mockFileCopier.CustomResults = new CopyFileResult[totalCopyAttempts];
                mockFileCopier.CustomResults[0] = new CopyFileResult(CopyFileResult.ResultCode.DestinationPathError);
                for(int counter = 1; counter < totalCopyAttempts; counter ++)
                {
                    mockFileCopier.CustomResults[counter] = new CopyFileResult(CopyFileResult.ResultCode.SourcePathError);
                };
                var destinationResult = await distributedCopier.TryCopyAndPutAsync(
                    new OperationContext(context),
                    hashWithLocations,
                    handleCopyAsync: tpl => Task.FromResult(new PutResult(hash, 99)));

                destinationResult.ShouldBeError();
                destinationResult.ErrorMessage.Should().Contain(hash.ToShortString());
                mockFileCopier.CopyAttempts.Should().Be(totalCopyAttempts);
            }
        }

        public static (TestDistributedContentCopier, MockFileCopier) CreateMocks(
            IAbsFileSystem fileSystem,
            AbsolutePath rootDirectory,
            TimeSpan retryInterval,
            int retries = 1)
        {
            var mockFileCopier = new MockFileCopier();
            var existenceChecker = new TestFileCopier();
            var contentCopier = new TestDistributedContentCopier(
                rootDirectory,
                // Need to use exactly one retry.
                new DistributedContentStoreSettings()
                {
                    RetryIntervalForCopies = Enumerable.Range(0, retries).Select(r => retryInterval).ToArray(),
                    TrustedHashFileSizeBoundary = long.MaxValue // Disable trusted hash because we never actually move bytes and thus the hasher thinks there is a mismatch.
                },
                fileSystem,
                mockFileCopier,
                existenceChecker,
                copyRequester: null,
                new TestDistributedContentCopier.NoOpPathTransformer(rootDirectory));
            return (contentCopier, mockFileCopier);
        }

        public class MockPathTransformer : IPathTransformer
        {
            /// <inheritdoc />
            public MachineLocation GetLocalMachineLocation(AbsolutePath cacheRoot) => new MachineLocation("");

            /// <inheritdoc />
            public PathBase GeneratePath(ContentHash contentHash, byte[] contentLocationIdContent) => null;

            /// <inheritdoc />
            public byte[] GetPathLocation(PathBase path) => new byte[] { };
        }

        public class MockFileCopier : IFileCopier
        {
            public int CopyAttempts = 0;
#pragma warning disable 649
            public CopyFileResult CopyToAsyncResult = CopyFileResult.Success;
#pragma warning restore 649
            public CopyFileResult[] CustomResults;

            /// <inheritdoc />
            public Task<CopyFileResult> CopyToAsync(PathBase sourcePath, Stream destinationStream, long expectedContentSize, CancellationToken cancellationToken)
            {
                CopyAttempts++;
                if (CustomResults != null)
                {
                    return Task.FromResult(CustomResults[CopyAttempts - 1]);
                }
                return Task.FromResult(CopyToAsyncResult);
            }

#pragma warning disable 649
            public FileExistenceResult CheckFileExistsAsyncResult;
#pragma warning restore 649

            /// <inheritdoc />
            public Task<FileExistenceResult> CheckFileExistsAsync(PathBase path, TimeSpan timeout, CancellationToken cancellationToken)
                => Task.FromResult(CheckFileExistsAsyncResult);
        }
    }

    public class TestDistributedContentCopier : DistributedContentCopier<AbsolutePath>, IDistributedContentCopierHost
    {
        public readonly NoOpPathTransformer PathTransformer;

        public TestDistributedContentCopier(
            AbsolutePath workingDirectory,
            DistributedContentStoreSettings settings,
            IAbsFileSystem fileSystem,
            IFileCopier<AbsolutePath> fileCopier,
            IFileExistenceChecker<AbsolutePath> fileExistenceChecker,
            IContentCommunicationManager copyRequester,
            IPathTransformer<AbsolutePath> pathTransformer)
            : base(settings, fileSystem, fileCopier, fileExistenceChecker, copyRequester, pathTransformer, TestSystemClock.Instance)
        {
            WorkingFolder = workingDirectory;
            PathTransformer = pathTransformer as NoOpPathTransformer;
        }

        public AbsolutePath WorkingFolder { get; }

        public void ReportReputation(MachineLocation location, MachineReputation reputation)
        {
        }

        protected override Task<CopyFileResult> CopyFileAsync(IFileCopier<AbsolutePath> copier, AbsolutePath sourcePath, AbsolutePath destinationPath, long expectedContentSize, bool overwrite, CancellationToken cancellationToken)
        {
            return copier.CopyToAsync(sourcePath, null, expectedContentSize, cancellationToken);
        }

        internal Task<PutResult> TryCopyAndPutAsync(OperationContext operationContext, ContentHashWithSizeAndLocations hashWithLocations, Func<(CopyFileResult copyResult, AbsolutePath tempLocation, int attemptCount), Task<PutResult>> handleCopyAsync)
        {
            return base.TryCopyAndPutAsync(operationContext, this, hashWithLocations, handleCopyAsync);
        }

        public class NoOpPathTransformer : TestPathTransformer
        {
            private readonly AbsolutePath _root;

            public byte[] LastContentLocation { get; set; }

            public NoOpPathTransformer(AbsolutePath root)
            {
                _root = root;
            }
            public override AbsolutePath GeneratePath(ContentHash contentHash, byte[] contentLocationIdContent)
            {
                LastContentLocation = contentLocationIdContent;
                return _root;
            }
        }
    }
}
