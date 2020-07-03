// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using BuildXL.Engine;
using BuildXL.Native.IO;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Graph;
using BuildXL.Pips.DirectedGraph;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using Mount=BuildXL.Utilities.Configuration.Mutable.Mount;
using BuildXL.Processes;
using BuildXL.Utilities.Collections;
using BuildXL.Engine.Tracing;

namespace Test.BuildXL.Engine
{
    public class MountScrubberTests : TemporaryStorageTestBase
    {
        private readonly LoggingConfiguration m_loggingConfiguration = new LoggingConfiguration();

        private DirectoryScrubber Scrubber { get; }

        private CancellationTokenSource m_cancellationTokenSource { get; }

        public MountScrubberTests(ITestOutputHelper output)
            : base(output)
        {
            RegisterEventSource(global::BuildXL.Engine.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.Scheduler.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.Pips.ETWLogger.Log);
            RegisterEventSource(global::BuildXL.Processes.ETWLogger.Log);

            m_cancellationTokenSource = new CancellationTokenSource();
            Scrubber = new DirectoryScrubber(
               m_cancellationTokenSource.Token,
               LoggingContext,
               m_loggingConfiguration,
               maxDegreeParallelism: 2);
        }

        [Fact]
        public void DoNotScrubDeclaredOutputDirectories()
        {
            string rootDirectory = Path.Combine(TemporaryDirectory, nameof(ScrubFilesAndDirectories));
            string a = WriteFile(Path.Combine(rootDirectory, "1", "1", "out.txt"));
            var inBuild = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.GetDirectoryName(a) };
            Scrubber.RemoveExtraneousFilesAndDirectories(
                isPathInBuild: path => inBuild.Contains(path),
                pathsToScrub: new[] { Path.GetDirectoryName(a) },
                blockedPaths: new string[] { },
                nonDeletableRootDirectories: new string[0]);

            XAssert.IsTrue(File.Exists(a));
        }

        [Fact(Skip = "Investigate why this test fails randomly in the OssRename branch")]
        public void ScrubFilesAndDirectories()
        {
            // Create a layout with various paths. We will clean at the root dir.
            // Some files will be in the build, some out, and some paths excluded
            string rootDirectory = Path.Combine(TemporaryDirectory, nameof(ScrubFilesAndDirectories));
            string a = WriteFile(Path.Combine(rootDirectory, "1", "1", "out.txt"));
            string b = WriteFile(Path.Combine(rootDirectory, "1", "out.txt"));
            string c = WriteFile(Path.Combine(rootDirectory, "2", "1", "in.txt"));
            string d = WriteFile(Path.Combine(rootDirectory, "2", "2", "in.txt"));
            string e = WriteFile(Path.Combine(rootDirectory, "2", "2", "out.txt"));
            string f = WriteFile(Path.Combine(rootDirectory, "3", "out.txt"));
            string g = WriteFile(Path.Combine(rootDirectory, "4", "out.txt"));
            string h = WriteFile(Path.Combine(rootDirectory, "5", "6", "out.txt"));
            string i = WriteFile(Path.Combine(rootDirectory, "5", "out.txt"));

            var inBuild = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { c, d, Path.GetDirectoryName(f) };

            Scrubber.RemoveExtraneousFilesAndDirectories(
                pathsToScrub: new[] { rootDirectory },
                isPathInBuild: path => inBuild.Contains(path),
                blockedPaths: new[] { Path.Combine(rootDirectory, "1"), Path.Combine(rootDirectory, "5", "6") },
                nonDeletableRootDirectories: new string[0]);

            // Files in the "1" directory should still exist even though they were not in the build, since that directory was excluded.
            XAssert.IsTrue(File.Exists(a));
            XAssert.IsTrue(File.Exists(b));

            // The files that were in the build should still exist.
            XAssert.IsTrue(File.Exists(c));
            XAssert.IsTrue(File.Exists(d));
            XAssert.IsTrue(File.Exists(f));

            // File in the blocked path still exists.
            XAssert.IsTrue(File.Exists(h));

            // The file outside of the build should not exist.
            XAssert.IsFalse(File.Exists(e));
            XAssert.IsFalse(Directory.Exists(Path.GetDirectoryName(g)));
            XAssert.IsFalse(File.Exists(i));
        }

        [Fact]
        public void ScrubbingDirectoriesWithMounts()
        {
            string rootDirectory = Path.Combine(TemporaryDirectory, nameof(ScrubbingDirectoriesWithMounts));
            const string NonScrubbableMountName = "NonScrubbable";
            string nonScrubbableMountPath = Path.Combine(rootDirectory, NonScrubbableMountName);
            const string ScrubbableMountName = "Scrubbable";
            string scrubbableMountPath = Path.Combine(rootDirectory, ScrubbableMountName);
            const string NestedScrubbableMountName = "NestedScrubbable";
            string nestedScrubbableMountPath = Path.Combine(scrubbableMountPath, NestedScrubbableMountName);

            MountPathExpander mountPathExpander =
                CreateMountPathExpander(
                    new TestMount(NonScrubbableMountName, nonScrubbableMountPath, MountFeatures.Writable | MountFeatures.Readable),
                    new TestMount(ScrubbableMountName, scrubbableMountPath, MountFeatures.Scrubbable),
                    new TestMount(NestedScrubbableMountName, nestedScrubbableMountPath, MountFeatures.Scrubbable));
            string f = WriteFile(Path.Combine(nonScrubbableMountPath, "D", "f"));
            string g1 = WriteFile(Path.Combine(scrubbableMountPath, "D", "g1"));
            string g2 = WriteFile(Path.Combine(scrubbableMountPath, "D", "g2"));
            string h = WriteFile(Path.Combine(scrubbableMountPath, "D", "E", "h"));
            string i = WriteFile(Path.Combine(scrubbableMountPath, "D", "F", "i"));
            string j = WriteFile(Path.Combine(nestedScrubbableMountPath, "D", "j"));
            var inBuild = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.Combine(scrubbableMountPath, "D", "E"), g1 };

            Scrubber.RemoveExtraneousFilesAndDirectories(
                path => inBuild.Contains(path),
                pathsToScrub: new[] { Path.Combine(nonScrubbableMountPath, "D"), scrubbableMountPath },
                blockedPaths: new string[0],
                nonDeletableRootDirectories: new string[0],
                mountPathExpander: mountPathExpander);

            // NonScrubbable\D produces a warning.
            AssertWarningEventLogged(global::BuildXL.Engine.Tracing.LogEventId.ScrubbingFailedBecauseDirectoryIsNotScrubbable);

            // f is in NonScrubbable.
            XAssert.IsTrue(File.Exists(f));

            // g1 & h (via Scrubbable\D\E) is in build.
            XAssert.IsTrue(File.Exists(g1));
            XAssert.IsTrue(File.Exists(h));

            // NestedScrubbable, although not in build, but is a mount root.
            XAssert.IsTrue(Directory.Exists(nestedScrubbableMountPath));

            // Scrubbed files/directories.
            XAssert.IsFalse(File.Exists(g2));
            XAssert.IsFalse(File.Exists(i));
            XAssert.IsFalse(Directory.Exists(Path.Combine(scrubbableMountPath, "D", "F")));
            XAssert.IsFalse(File.Exists(j));
            XAssert.IsFalse(Directory.Exists(Path.Combine(nestedScrubbableMountPath, "D")));
        }

        [Fact]
        public void ScrubFileDirectoriesWithPipGraph()
        {
            string rootDirectory = Path.Combine(TemporaryDirectory, nameof(ScrubFileDirectoriesWithPipGraph));
            string sourceRoot = Path.Combine(rootDirectory, "Src");
            string outputRoot = Path.Combine(rootDirectory, "Out");
            string targetRoot = Path.Combine(rootDirectory, "Target");

            var pathTable = new PathTable();

            using (TestEnv env = TestEnv.CreateTestEnvWithPausedScheduler(
                new List<IMount> {
                    new Mount()
                    {
                        Name = PathAtom.Create(pathTable.StringTable, "testRoot"),
                        Path = AbsolutePath.Create(pathTable, TemporaryDirectory),
                        IsWritable = true,
                        IsReadable = true,
                        IsScrubbable = true,
                        AllowCreateDirectory = true,
                    }
                },
                pathTable)
            )
            {
                string inputFilePath = Path.Combine(sourceRoot, "input.txt");
                WriteFile(inputFilePath);
                string outputFilePath = Path.Combine(outputRoot, "output.txt");
                WriteFile(outputFilePath);

                string tempOutputDirectoryPath = Path.Combine(outputRoot, "TempOutDir");
                string tempOutputPath = Path.Combine(tempOutputDirectoryPath, "tempOutputInDir.txt");
                Directory.CreateDirectory(tempOutputDirectoryPath);

                string optionalOutputDirectoryPath = Path.Combine(outputRoot, "OptionalOutDir");
                string optionalOutputPath = Path.Combine(optionalOutputDirectoryPath, "optionalOutputInDir.txt");
                Directory.CreateDirectory(optionalOutputDirectoryPath);

                string targetFileInOutputDirectoryPath = Path.Combine(targetRoot, "targetInDir.txt");
                WriteFile(targetFileInOutputDirectoryPath);

                string outputDirectoryPath = Path.Combine(outputRoot, "OutDir");
                string outputFileInOutputDirectoryPath = Path.Combine(outputDirectoryPath, "outputInDir.txt");
                WriteFile(outputFileInOutputDirectoryPath);

                string sharedOutputDirectoryPath = Path.Combine(outputRoot, "SharedOutDir");
                string outputFileInOutputSharedDirectoryPath = Path.Combine(sharedOutputDirectoryPath, "outputInSharedDir.txt");
                WriteFile(outputFileInOutputSharedDirectoryPath);

                string junkOutputPath = Path.Combine(outputRoot, "junk.txt");
                WriteFile(junkOutputPath);
                string junkOutputInOutputDirectoryPath = Path.Combine(outputDirectoryPath, "junkInDir.txt");
                WriteFile(junkOutputInOutputDirectoryPath);
                string junkTempOutputPath = Path.Combine(tempOutputDirectoryPath, "junkTempOutput.txt");
                WriteFile(junkTempOutputPath);
                string junkOptionalOutputPath = Path.Combine(optionalOutputDirectoryPath, "junkOptionalOutput.txt");
                WriteFile(junkOptionalOutputPath);
                string junkDirectoryPath = Path.Combine(outputRoot, "JunkDir");
                string junkFileInJunkDirectoryPath = Path.Combine(junkDirectoryPath, "junkInJunkDir.txt");
                WriteFile(junkFileInJunkDirectoryPath);

                var pipBuilder = CreatePipBuilderWithTag(env, nameof(ScrubFileDirectoriesWithPipGraph));
                FileArtifact input = env.Paths.CreateSourceFile(env.Paths.CreateAbsolutePath(inputFilePath));
                pipBuilder.AddInputFile(input);

                AbsolutePath output = env.Paths.CreateAbsolutePath(outputFilePath);
                pipBuilder.AddOutputFile(output);

                AbsolutePath tempOutput = env.Paths.CreateAbsolutePath(tempOutputPath);
                pipBuilder.AddOutputFile(tempOutput, FileExistence.Temporary);

                AbsolutePath optionalOutput = env.Paths.CreateAbsolutePath(optionalOutputPath);
                pipBuilder.AddOutputFile(optionalOutput, FileExistence.Optional);

                AbsolutePath outputDirectory = env.Paths.CreateAbsolutePath(outputDirectoryPath);
                pipBuilder.AddOutputDirectory(outputDirectory);

                AbsolutePath targetRootAbsolutePath = env.Paths.CreateAbsolutePath(targetRoot);
                pipBuilder.AddOutputDirectory(targetRootAbsolutePath);

                AbsolutePath sharedOutputDirectory = env.Paths.CreateAbsolutePath(sharedOutputDirectoryPath);
                pipBuilder.AddOutputDirectory(sharedOutputDirectory, SealDirectoryKind.SharedOpaque);

                env.PipConstructionHelper.AddProcess(pipBuilder);
                PipGraph pipGraph = AssertSuccessGraphBuilding(env);
                RunScrubberWithPipGraph(env, pipGraph, pathsToScrub: new[] { outputRoot, targetRoot });

                // All non-junk files/directories should be preserved, except ... (see below)
                XAssert.IsTrue(File.Exists(inputFilePath));
                XAssert.IsTrue(File.Exists(outputFilePath));
                XAssert.IsTrue(Directory.Exists(tempOutputDirectoryPath));
                XAssert.IsTrue(Directory.Exists(optionalOutputDirectoryPath));
                XAssert.IsTrue(Directory.Exists(outputDirectoryPath));
                XAssert.IsTrue(Directory.Exists(sharedOutputDirectoryPath));

                // Shared output directory is always scrubbed, and thus its contents should be removed.
                XAssert.IsFalse(File.Exists(outputFileInOutputSharedDirectoryPath));

                // All junk files/directories should be removed, except ... (see below).
                XAssert.IsFalse(File.Exists(junkOutputPath));
                XAssert.IsFalse(File.Exists(junkTempOutputPath));
                XAssert.IsFalse(File.Exists(junkOptionalOutputPath));
                XAssert.IsFalse(Directory.Exists(junkDirectoryPath));

                // Junk output in an output directory is not removed because
                // when we run again the pip (can be from cache), the whole output directory will be removed.
                XAssert.IsTrue(File.Exists(junkOutputInOutputDirectoryPath));
            }
        }

        [Fact]
        public void DontScrubBlockedPathsEvenIfAskedTo()
        {
            string rootDirectory = Path.Combine(TemporaryDirectory, nameof(ScrubFilesAndDirectories));
            string a = WriteFile(Path.Combine(rootDirectory, "a", "b", "c", "out.txt"));

            var inBuild = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { };

            Scrubber.RemoveExtraneousFilesAndDirectories(
                path => inBuild.Contains(path),
                pathsToScrub: new[] { rootDirectory },
                blockedPaths: new[] { rootDirectory },
                nonDeletableRootDirectories: new string[0]);

            // The file should still exist since it is under a blocked path
            XAssert.IsTrue(File.Exists(a));
        }

        [Fact]
        public void DeleteFilesCanDeleteFile()
        {
            string rootDir = Path.Combine(TemporaryDirectory, nameof(DeleteFilesCanDeleteFile));

            string fullFilePath = WriteFile(Path.Combine(rootDir, "out.txt"));
            XAssert.IsTrue(File.Exists(fullFilePath));

            var numDeleted = Scrubber.DeleteFiles(new[] { fullFilePath });

            XAssert.IsFalse(File.Exists(fullFilePath));
            XAssert.AreEqual(1, numDeleted);
        }

        [Fact]
        public void DeleteFilesCancellationDoesNotCrash()
        {
            const int FileDeletionsAllowed = 2;
            var testHook = new DirectoryScrubber.TestHooks
            {
                OnDeletion = new Action<string, int>((path, numDeletedSoFar) =>
                {
                    if (numDeletedSoFar > FileDeletionsAllowed) { m_cancellationTokenSource.Cancel(); }
                })
            };

            string rootDir = Path.Combine(TemporaryDirectory, nameof(DeleteFilesCanDeleteFile));

            List<string> files = new List<string>();

            for (var i = 0; i < FileDeletionsAllowed * 3; i++)
            {
                string fullFilePath = WriteFile(Path.Combine(rootDir, $"out{i}.txt"));
                XAssert.FileExists(fullFilePath);
                files.Add(fullFilePath);
            }

            XAssert.IsFalse(m_cancellationTokenSource.IsCancellationRequested);

            var numDeleted = Scrubber.DeleteFiles(files.ToArray(), testHook: testHook);
            XAssert.IsTrue(m_cancellationTokenSource.IsCancellationRequested);
            XAssert.Equals(FileDeletionsAllowed, numDeleted);
        }

        [Fact]
        public void DeleteFilesWithPreExistingCancellationDoesNotCrash()
        {
            string rootDir = Path.Combine(TemporaryDirectory, nameof(DeleteFilesCanDeleteFile));

            string fullFilePath = WriteFile(Path.Combine(rootDir, "out.txt"));
            XAssert.FileExists(fullFilePath);

            m_cancellationTokenSource.Cancel();
            var numDeleted = Scrubber.DeleteFiles(new[] { fullFilePath });

            XAssert.FileExists(fullFilePath);
            XAssert.AreEqual(0, numDeleted);
        }

        [TheoryIfSupported(requiresSymlinkPermission: true)]
        [InlineData(true)]
        [InlineData(false)]
        public void DeleteFilesDeletesSymlinkButNotTarget(bool useRelativeTargetForSymlink)
        {
            string rootDir = Path.Combine(TemporaryDirectory, nameof(DeleteFilesDeletesSymlinkButNotTarget));

            string fileBasename = "out.txt";
            string fullFilePath = WriteFile(Path.Combine(rootDir, fileBasename));
            XAssert.IsTrue(File.Exists(fullFilePath));

            string fullSymlinkPath = WriteSymlink(
                Path.Combine(rootDir, $"sym-{fileBasename}"),
                useRelativeTargetForSymlink ? fileBasename : fullFilePath,
                isTargetFile: true);
            XAssert.IsTrue(FileUtilities.FileExistsNoFollow(fullSymlinkPath));

            var numDeleted = Scrubber.DeleteFiles(new[] { fullSymlinkPath });

            XAssert.AreEqual(1, numDeleted);
            XAssert.IsFalse(File.Exists(fullSymlinkPath));
            XAssert.IsTrue(File.Exists(fullFilePath));
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public void DeleteFilesCanDeleteDirectorySymlink()
        {
            string rootDir = Path.Combine(TemporaryDirectory, nameof(DeleteFilesCanDeleteDirectorySymlink));
            string fullTargetDirPath = Path.Combine(rootDir, "target-dir");
            Directory.CreateDirectory(fullTargetDirPath);
            XAssert.IsTrue(Directory.Exists(fullTargetDirPath));

            string fullSymlinkPath = WriteSymlink(Path.Combine(rootDir, "directory symlink"), fullTargetDirPath, isTargetFile: false);
            XAssert.IsTrue(FileUtilities.FileExistsNoFollow(fullSymlinkPath));

            var numDeleted = Scrubber.DeleteFiles(new[] { fullSymlinkPath });

            XAssert.AreEqual(1, numDeleted);
            XAssert.IsFalse(File.Exists(fullSymlinkPath));
            XAssert.IsTrue(Directory.Exists(fullTargetDirPath));
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public void DirectorySymlinksAreTraversed()
        {
            string rootDir = Path.Combine(TemporaryDirectory, nameof(DirectorySymlinksAreTraversed));
            string fullTargetDirPath = Path.Combine(rootDir, "target-dir");
            Directory.CreateDirectory(fullTargetDirPath);

            var fileUnderTarget = Path.Combine(fullTargetDirPath, "file.txt");
            File.WriteAllText(fileUnderTarget, "content");

            string rootToScrub = Path.Combine(rootDir, "root-to-scrub");
            Directory.CreateDirectory(rootToScrub);

            string fullSymlinkPath = WriteSymlink(Path.Combine(rootToScrub, "directory symlink"), fullTargetDirPath, isTargetFile: false);
            XAssert.IsTrue(FileUtilities.FileExistsNoFollow(fullSymlinkPath));

            // Scrub starting on a root dir that contains a symlink directory. We should be able to find the file underneath and delete it
            // We consider all artifacts to be not part of the build, so symlink directories are followed and all files are deleted
            Scrubber.RemoveExtraneousFilesAndDirectories(
                        isPathInBuild: path => false,
                        pathsToScrub: new[] { rootDir },
                        blockedPaths: CollectionUtilities.EmptyArray<string>(),
                        nonDeletableRootDirectories: CollectionUtilities.EmptyArray<string>());

            XAssert.IsFalse(File.Exists(fileUnderTarget));
            XAssert.Equals(OperatingSystemHelper.IsMacOS, !Directory.Exists(fullSymlinkPath));
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public void DirectorySymlinksUnderSharedOpaquesArePreservedIfNonEmpty()
        {
            string rootDir = Path.Combine(TemporaryDirectory, nameof(DirectorySymlinksUnderSharedOpaquesArePreservedIfNonEmpty));
            string fullTargetDirPath = Path.Combine(rootDir, "target-dir");
            Directory.CreateDirectory(fullTargetDirPath);
            XAssert.IsTrue(Directory.Exists(fullTargetDirPath));

            var fileUnderTarget = Path.Combine(fullTargetDirPath, "file.txt");
            File.WriteAllText(fileUnderTarget, "content");

            string fullSymlinkPath = WriteSymlink(Path.Combine(rootDir, "directory symlink"), fullTargetDirPath, isTargetFile: false);
            XAssert.IsTrue(FileUtilities.FileExistsNoFollow(fullSymlinkPath));

            if (OperatingSystemHelper.IsMacOS)
            {
                SharedOpaqueOutputHelper.EnforceFileIsSharedOpaqueOutput(fullSymlinkPath);
                XAssert.IsTrue(SharedOpaqueOutputHelper.IsSharedOpaqueOutput(fullSymlinkPath));
            }

            // This is somewhat subtle. On Windows, IsSharedOpaqueOutput will say yes for any directory, including symlink directories. This means they won't be
            // considered part of the build and therefore should be traversed.
            // So if the symlink is traversed, fileUnderTarget will be found, which is not a shared opaque output. So the file won't be deleted. And
            // so nor the symlink directory. If the symlink directory wasn't traversed, then it would be deleted.
            Scrubber.RemoveExtraneousFilesAndDirectories(
                isPathInBuild: path => !SharedOpaqueOutputHelper.IsSharedOpaqueOutput(path), 
                pathsToScrub: new[] { rootDir }, 
                blockedPaths: CollectionUtilities.EmptyArray<string>(), 
                nonDeletableRootDirectories: CollectionUtilities.EmptyArray<string>());

            XAssert.FileExists(fileUnderTarget);

            // On Mac:
            //   - any symlink is a file, any file under shared opaque dir gets scrubber ==> fullSymlinkPath should be scrubbed
            //
            // On Windows:
            //   - directories under shared opaques are always removed unless they have files underneath that shouldn't be 
            //     removed. This test verifies this behavior also applies to symlink directories
            XAssert.AreEqual(!OperatingSystemHelper.IsMacOS, Directory.Exists(fullSymlinkPath));
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public void SymlinkDirectoriesDoNotIntroduceDuplicateWork()
        {
            // We are creating this layout
            //
            // root
            // -- real-dir
            //    -- symlink-dir -> real-dir
            //       --file.txt
            //
            // so there is in fact a cycle, where root/real-dir/symlink-dir/.../symlink-dir/... is a valid path
            // The fact that scrubbing finishes proves we are deduping work

            string rootDir = Path.Combine(TemporaryDirectory, nameof(SymlinkDirectoriesDoNotIntroduceDuplicateWork));
            string realDirectory = Path.Combine(rootDir, "real-dir");
            Directory.CreateDirectory(realDirectory);

            string symlinkDirectory = WriteSymlink(Path.Combine(realDirectory, "symlink-dir"), rootDir, isTargetFile: false);
            XAssert.IsTrue(FileUtilities.FileExistsNoFollow(symlinkDirectory));

            var fileUnderSymlinkDir = Path.Combine(symlinkDirectory, "file.txt");
            File.WriteAllText(fileUnderSymlinkDir, "content");

            try
            {
                Scrubber.RemoveExtraneousFilesAndDirectories(
                    isPathInBuild: path => false,
                    pathsToScrub: new[] { rootDir },
                    blockedPaths: CollectionUtilities.EmptyArray<string>(),
                    nonDeletableRootDirectories: CollectionUtilities.EmptyArray<string>());

                XAssert.IsFalse(File.Exists(fileUnderSymlinkDir));
                XAssert.Equals(!OperatingSystemHelper.IsMacOS, Directory.Exists(realDirectory));
                XAssert.Equals(!OperatingSystemHelper.IsMacOS, Directory.Exists(symlinkDirectory));
            }
            finally 
            {
                // On Windows, the temp directory cleaner has problems with cycles. So let's remove the symlink dir explicitly here
                if (!OperatingSystemHelper.IsMacOS)
                {
                    Directory.Delete(symlinkDirectory);
                }
            }
        }

        [TheoryIfSupported(requiresSymlinkPermission: true)]
        [InlineData(true)]
        [InlineData(false)]
        public void DeleteFilesCanDeleteSymlinkToAbsentFile(bool declareSymlinkTargetAsFile)
        {
            string rootDir = Path.Combine(TemporaryDirectory, nameof(DeleteFilesCanDeleteSymlinkToAbsentFile));
            string fullSymlinkPath = WriteSymlink(Path.Combine(rootDir, "symlink-to-absent"), "non-existent-target", isTargetFile: declareSymlinkTargetAsFile);
            XAssert.IsTrue(FileUtilities.FileExistsNoFollow(fullSymlinkPath));

            var numDeleted = Scrubber.DeleteFiles(new[] { fullSymlinkPath });

            XAssert.AreEqual(1, numDeleted);
            XAssert.IsFalse(File.Exists(fullSymlinkPath));
        }

        [Fact]
        public void DeleteFilesCannotDeleteFolder()
        {
            string rootDir = Path.Combine(TemporaryDirectory, nameof(DeleteFilesCannotDeleteFolder));
            Directory.CreateDirectory(rootDir);
            XAssert.IsTrue(Directory.Exists(rootDir));

            var numDeleted = Scrubber.DeleteFiles(new[] { rootDir });
            XAssert.IsTrue(Directory.Exists(rootDir));
            XAssert.AreEqual(0, numDeleted);
        }

        [Fact]
        public void DeleteFilesHandlesAbsentFiles()
        {
            string rootDir = Path.Combine(TemporaryDirectory, nameof(DeleteFilesHandlesAbsentFiles));
            Directory.CreateDirectory(rootDir);
            XAssert.IsTrue(Directory.Exists(rootDir));
            
            var absentFile = Path.Combine(rootDir, "a b s e n t");
            XAssert.IsFalse(File.Exists(absentFile));

            var numDeleted = Scrubber.DeleteFiles(new[] { absentFile });
            XAssert.IsFalse(File.Exists(absentFile));
            XAssert.AreEqual(0, numDeleted);
        }

        [FactIfSupported(requiresSymlinkPermission: true)]
        public void DeleteFilesHandlesMixedEntries()
        {
            string rootDir = Path.Combine(TemporaryDirectory, nameof(DeleteFilesHandlesMixedEntries));
            var files = new[]
            {
                (delete: false, path: rootDir),                                                   // directory
                (delete: true,  path: WriteFile(Path.Combine(rootDir, "file"))),                  // file
                (delete: false, path: Path.Combine(rootDir, "absent-file")),                      // absent
                (delete: true,  path: WriteSymlink(Path.Combine(rootDir, "sym-file"), "file")),   // symlink to file
                (delete: true,  path: WriteSymlink(Path.Combine(rootDir, "sym-abs"), "abent")),   // symlink to absent
            };

            var numDeleted = Scrubber.DeleteFiles(files.Select(t => t.path).ToArray());
            var expectedNumDeleted = files.Where(t => t.delete).Count();
            XAssert.AreEqual(expectedNumDeleted, numDeleted);
            foreach (var tuple in files.Where(t => t.delete))
            {
                XAssert.IsFalse(File.Exists(tuple.path));
            }
        }

        [Theory]
        [InlineData(100)]
        public void DeleteFilesMiniStressTest(int numFiles)
        {
            string rootDir = Path.Combine(TemporaryDirectory, nameof(DeleteFilesMiniStressTest));
            var files = Enumerable.Range(0, numFiles).Select(i => WriteFile(Path.Combine(rootDir, $"file-{i}"))).ToArray();

            var numDeleted = Scrubber.DeleteFiles(files);

            XAssert.AreEqual(numFiles, numDeleted);
            foreach (var file in files)
            {
                XAssert.IsFalse(File.Exists(file));
            }
        }

        private void RunScrubberWithPipGraph(
            TestEnv env,
            PipGraph pipGraph,
            string[] pathsToScrub,
            MountPathExpander mountPathExpander = null)
        {
            bool removed = Scrubber.RemoveExtraneousFilesAndDirectories(
                isPathInBuild: p => pipGraph.IsPathInBuild(env.Paths.CreateAbsolutePath(p)),
                pathsToScrub: pathsToScrub,
                blockedPaths: new string[0],
                nonDeletableRootDirectories: pipGraph.AllDirectoriesContainingOutputs().Select(p => env.Paths.Expand(p)),
                mountPathExpander: mountPathExpander);
            XAssert.IsTrue(removed);
        }

        private static string WriteFile(string path)
        {
            // ReSharper disable once AssignNullToNotNullAttribute
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "file");
            return path;
        }

        private static string WriteSymlink(string symlinkPath, string target, bool isTargetFile = true)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(symlinkPath));
            XAssert.IsTrue(FileUtilities.TryCreateSymbolicLink(symlinkPath, target, isTargetFile).Succeeded);
            return symlinkPath;
        }

        [Flags]
        private enum MountFeatures
        {
            None = 0,
            Scrubbable = 1,
            Writable = 2,
            Readable = 4,
            Hashable = 8
        }

        private sealed class TestMount
        {
            public MountFeatures Features { get; }

            public string Name { get; }

            public string Path { get; }

            public TestMount(string name, string path, MountFeatures features)
            {
                Name = name;
                Path = path;
                Features = features;
            }
        }

        private static MountPathExpander CreateMountPathExpander(params TestMount[] mounts)
        {
            var pathTable = new PathTable();
            var mountPathExpander = new MountPathExpander(pathTable);
            foreach (var mount in mounts)
            {
                mountPathExpander.Add(
                    pathTable,
                    new Mount
                    {
                        Name = PathAtom.Create(pathTable.StringTable, mount.Name),
                        Path = AbsolutePath.Create(pathTable, mount.Path),
                        IsReadable = mount.Features.HasFlag(MountFeatures.Readable),
                        IsWritable = mount.Features.HasFlag(MountFeatures.Writable),
                        IsScrubbable = mount.Features.HasFlag(MountFeatures.Scrubbable),
                        TrackSourceFileChanges = mount.Features.HasFlag(MountFeatures.Hashable)
                    });
            }

            return mountPathExpander;
        }

        private static ProcessBuilder CreatePipBuilderWithTag(TestEnv env, string tag = null)
        {
            var exe = FileArtifact.CreateSourceFile(AbsolutePath.Create(env.Context.PathTable, @"\\dummyPath\DummyFile.exe"));

            var processBuilder = ProcessBuilder.Create(env.PathTable, env.PipDataBuilderPool.GetInstance());
            processBuilder.Executable = exe;
            processBuilder.AddInputFile(exe);
            if (tag != null)
            {
                processBuilder.AddTags(env.PathTable.StringTable, tag);
            }

            return processBuilder;
        }

        private static PipGraph AssertSuccessGraphBuilding(TestEnv env)
        {
            var builder = env.PipGraph as PipGraph.Builder;

            XAssert.IsNotNull(builder);
            var pipGraph = builder.Build();
            XAssert.IsNotNull(pipGraph);
            return pipGraph;
        }
    }
}
