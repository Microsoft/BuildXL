// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Engine.Tracing;
using BuildXL.Native.IO;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using Test.BuildXL.EngineTestUtilities;
using Test.BuildXL.FrontEnd.Rush.IntegrationTests;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.FrontEnd.Rush
{
    [Trait("Category", "RushIntegrationTests")]
    public class RushIntegrationTests : RushIntegrationTestBase
    {
        public RushIntegrationTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void EndToEndPipExecutionWithDependencies()
        {
            // Create two projects A and B such that A -> B.
            var config = Build()
                .AddRushProject("@ms/project-A", "src/A", "module.exports = function A(){}")
                .AddRushProject("@ms/project-B", "src/B", "const A = require('@ms/project-A'); return A();", new string[] { "@ms/project-A"})
                .PersistSpecsAndGetConfiguration();

            var engineResult = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A"),
                ("src/B", "@ms/project-B"),
            });

            Assert.True(engineResult.IsSuccess);
            
            // Let's do some basic graph validations
            var processes = engineResult.EngineState.PipGraph.RetrievePipsOfType(global::BuildXL.Pips.Operations.PipType.Process).ToList();
            // There should be two process pips
            Assert.Equal(2, processes.Count);

            // Project A depends on project B
            var projectAPip = processes.First(pip => ((Process)pip).Provenance.OutputValueSymbol.ToString(engineResult.EngineState.SymbolTable).Contains("project_A"));
            var projectBPip = processes.First(pip => ((Process)pip).Provenance.OutputValueSymbol.ToString(engineResult.EngineState.SymbolTable).Contains("project_B"));
            Assert.True(engineResult.EngineState.PipGraph.IsReachableFrom(projectAPip.PipId.ToNodeId(), projectBPip.PipId.ToNodeId()));
        }

        [Fact]
        public void RushCacheGraphBehavior()
        {
            var testCache = new TestCache();

            var config = (CommandLineConfiguration)Build()
                    .AddRushProject("@ms/project-A", "src/A")
                    .PersistSpecsAndGetConfiguration();

            config.Cache.CacheGraph = true;
            config.Cache.AllowFetchingCachedGraphFromContentCache = true;
            config.Cache.Incremental = true;

            // First time the graph should be computed
            var engineResult = RunRushProjects(
                config, 
                new[] {("src/A", "@ms/project-A") },
                testCache);

            Assert.True(engineResult.IsSuccess);

            AssertInformationalEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.FrontEndStartEvaluateValues);
            AssertInformationalEventLogged(LogEventId.EndSerializingPipGraph);
            AssertLogContains(false, "Storing pip graph descriptor to cache: Status: Success");

            // The second build should fetch and reuse the graph from the cache
            engineResult = RunRushProjects(
                config, 
                new[] {("src/A", "@ms/project-A")},
                testCache);

            Assert.True(engineResult.IsSuccess);

            AssertInformationalEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.FrontEndStartEvaluateValues, count: 0);
            AssertInformationalEventLogged(LogEventId.EndDeserializingEngineState);
        }

        [Fact]
        public void ExposedEnvironmentVariablesBehavior()
        {
            var testCache = new TestCache();

            // Set env var 'Test' to an arbitrary value, but override that value in the main config, so
            // we actually don't depend on it
            Environment.SetEnvironmentVariable("Test", "2");

            var environment = new Dictionary<string, string>
            {
                ["PATH"] = PathToNodeFolder,
                ["Test"] = "3"
            };

            var config = (CommandLineConfiguration)Build(environment)
                    .AddRushProject("@ms/project-A", "src/A")
                    .PersistSpecsAndGetConfiguration();

            config.Cache.CacheGraph = true;
            config.Cache.AllowFetchingCachedGraphFromContentCache = true;
            config.Cache.Incremental = true;

            // First time the graph should be computed
            var engineResult = RunRushProjects(
                config,
                new[] { ("src/A", "@ms/project-A") },
                testCache);
            Assert.True(engineResult.IsSuccess);

            AssertInformationalEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.FrontEndStartEvaluateValues);
            AssertInformationalEventLogged(LogEventId.EndSerializingPipGraph);
            AssertLogContains(false, "Storing pip graph descriptor to cache: Status: Success");

            // Change the environment variable. Since we overrode the value, the second
            // build should fetch and reuse the graph from the cache
            Environment.SetEnvironmentVariable("Test", "3");
            engineResult = RunRushProjects(
                config,
                new[] { ("src/A", "@ms/project-A") },
                testCache);
            Assert.True(engineResult.IsSuccess);

            AssertInformationalEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.FrontEndStartEvaluateValues, count: 0);
            AssertInformationalEventLogged(LogEventId.EndDeserializingEngineState);
        }

        [Fact]
        public void PassthroughVariablesAreHonored()
        {
            var testCache = new TestCache();

            Environment.SetEnvironmentVariable("Test", "originalValue");

            var environment = new Dictionary<string, DiscriminatingUnion<string, UnitValue>> { 
                ["PATH"] = new DiscriminatingUnion<string, UnitValue>(PathToNodeFolder), 
                ["Test"] = new DiscriminatingUnion<string, UnitValue>(UnitValue.Unit) };

            var config = (CommandLineConfiguration)Build(environment)
                .AddRushProject("@ms/project-A", "src/A")
                .PersistSpecsAndGetConfiguration();

            config.Cache.CacheGraph = true;
            config.Cache.AllowFetchingCachedGraphFromContentCache = true;
            config.Cache.Incremental = true;

            // First time the graph should be computed
            var engineResult = RunRushProjects(
                config,
                new[] { ("src/A", "@ms/project-A") },
                testCache);

            Assert.True(engineResult.IsSuccess);

            AssertInformationalEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.FrontEndStartEvaluateValues);
            AssertInformationalEventLogged(LogEventId.EndSerializingPipGraph);
            AssertLogContains(false, "Storing pip graph descriptor to cache: Status: Success");

            Environment.SetEnvironmentVariable("Test", "modifiedValue");

            engineResult = RunRushProjects(
                config,
                new[] { ("src/A", "@ms/project-A") },
                testCache);

            Assert.True(engineResult.IsSuccess);

            AssertInformationalEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.FrontEndStartEvaluateValues, count: 0);
            AssertInformationalEventLogged(LogEventId.EndDeserializingEngineState);
        }

        [Fact]
        public void DuplicateProjectNamesAreBlocked()
        {
            // Create two projects with the same package name
            var config = Build()
                    .AddRushProject("@ms/project-A", "src/A")
                    .AddRushProject("@ms/project-A", "src/B")
                    .PersistSpecsAndGetConfiguration();

            var engineResult = RunRushProjects(config, new[] {
                ("src/A", "@ms/project-A"),
                ("src/B", "@ms/project-A"),
            });

            Assert.False(engineResult.IsSuccess);

            AssertErrorEventLogged(global::BuildXL.FrontEnd.JavaScript.Tracing.LogEventId.ProjectGraphConstructionError);
            AssertErrorEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.CannotBuildWorkspace);
        }

        [Fact]
        public void IntermediateDirectoryCasingIsPreserved()
        {
            var testCache = new TestCache();

            // Run a project that writes a file under a nested directory
            var config = (CommandLineConfiguration)Build()
                    .AddRushProjectWithExplicitVersions(
                        "@ms/project-A", 
                        "src/A", 
                        "var fs = require('fs'); fs.mkdirSync('CamelCasedLib'); fs.writeFileSync('CamelCasedLib/out.txt', 'hello');",
                        new[] { ("path", "0.12.7") })
                    .PersistSpecsAndGetConfiguration();

            config.Cache.CacheGraph = true;
            config.Cache.AllowFetchingCachedGraphFromContentCache = true;
            config.Cache.Incremental = true;

            var engineResult = RunRushProjects(
                config,
                new[] { ("src/A", "@ms/project-A") },
                testCache);

            // Make sure the directory was written with the expected case (via a case sensitive comparison)
            Assert.True(engineResult.IsSuccess);
            string intermediateDir = Directory.EnumerateDirectories(Path.Combine(SourceRoot, "src", "A"))
                .First(dir => dir.EndsWith("CamelCasedLib", StringComparison.Ordinal));
            Assert.NotNull(intermediateDir);

            // Delete the created directory and run from cache
            FileUtilities.DeleteDirectoryContents(intermediateDir, deleteRootDirectory: true);

            engineResult = RunRushProjects(
                config,
                new[] { ("src/A", "@ms/project-A") },
                testCache);

            // The result should have preserved casing
            Assert.True(engineResult.IsSuccess);
            intermediateDir = Directory.EnumerateDirectories(Path.Combine(SourceRoot, "src", "A"))
                .First(dir => dir.EndsWith("CamelCasedLib", StringComparison.Ordinal));
            Assert.NotNull(intermediateDir);
        }
    }
}
