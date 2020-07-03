﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Processes.Tracing;
using BuildXL.Utilities;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace IntegrationTest.BuildXL.Scheduler
{
    public class ErrorRegexTests : SchedulerIntegrationTestBase
    {
        private readonly List<string> m_loggedPipFailures;

        public ErrorRegexTests(ITestOutputHelper output) : base(output)
        {
            m_loggedPipFailures = new List<string>();
            ShouldCreateLogDir = true;
        }

        public static IEnumerable<object[]> Test1Data()
        {
            const bool SingleLineScanning = false;
            const bool MultiLineScanning = true;
            const string Text = @"
* BEFORE *
* <error> *
* err1 *
* </error> *
* AFTER *
* <error>err2</error> * <error>err3</error> *
";

            foreach (var useStdErr in new[] { true, false })
            {
                yield return new object[] { useStdErr, Text, "error", SingleLineScanning, @"
* <error> *
* </error> *
* <error>err2</error> * <error>err3</error> *" };

                yield return new object[] { useStdErr, Text, "(?s)error", MultiLineScanning, @"
error
error
error
error
error
error" };

                yield return new object[] { useStdErr, Text, "<error>[^<]*</error>", SingleLineScanning, @"
* <error>err2</error> * <error>err3</error> *" };

                yield return new object[] { useStdErr, Text, "(?s)<error>[^<]*</error>", MultiLineScanning, @"
<error> *
* err1 *
* </error>
<error>err2</error>
<error>err3</error>" };

                yield return new object[] { useStdErr, Text, "(?s)<error>[\\s*]*(?<ErrorMessage>.*?)[\\s*]*</error>", MultiLineScanning, @"
err1
err2
err3" };
            }
        }

        [Theory]
        [MemberData(nameof(Test1Data))]
        public void Test1(bool useStdErr, string text, string errRegex, bool enableMultiLineScanning, string expectedPrintedError)
        {
            EventListener.NestedLoggerHandler += eventData =>
            {
                if (eventData.EventId == (int)LogEventId.PipProcessError)
                {
                    var loggedMessage = eventData.Payload.ToArray()[5].ToString();
                    m_loggedPipFailures.Add(loggedMessage);
                }
            };

            var ops = SplitLines(text)
                .Select(l => Operation.Echo(l, useStdErr))
                .Concat(new[]
                {
                    Operation.WriteFile(CreateOutputFileArtifact()),
                    Operation.Fail()
                });
            var pipBuilder = CreatePipBuilder(ops);            
            pipBuilder.ErrorRegex = new RegexDescriptor(StringId.Create(Context.StringTable, errRegex), RegexOptions.None);
            pipBuilder.EnableMultiLineErrorScanning = enableMultiLineScanning;

            SchedulePipBuilder(pipBuilder);
            RunScheduler().AssertFailure();

            AssertErrorEventLogged(LogEventId.PipProcessError);
            XAssert.ArrayEqual(
                SplitLines(expectedPrintedError), 
                m_loggedPipFailures.SelectMany(SplitLines).ToArray());
        }

        [Theory]
        [InlineData(global::BuildXL.Utilities.Configuration.OutputReportingMode.FullOutputAlways)]
        [InlineData(global::BuildXL.Utilities.Configuration.OutputReportingMode.FullOutputOnError)]
        [InlineData(global::BuildXL.Utilities.Configuration.OutputReportingMode.FullOutputOnWarningOrError)]
        [InlineData(global::BuildXL.Utilities.Configuration.OutputReportingMode.TruncatedOutputOnError)]
        public void StdFileCopyTest(global::BuildXL.Utilities.Configuration.OutputReportingMode outputReportingMode)
        {
            EventListener.NestedLoggerHandler += eventData =>
            {
                if (eventData.EventId == (int)LogEventId.PipProcessError)
                {
                    var loggedMessage = eventData.Payload.ToArray()[5].ToString();
                    var extraLoggedMessage = eventData.Payload.ToArray()[6].ToString();
                    m_loggedPipFailures.Add(loggedMessage);
                    m_loggedPipFailures.Add(extraLoggedMessage);
                }
            };

            Configuration.Sandbox.OutputReportingMode = outputReportingMode;
            var text = @"
* BEFORE *
* <error> *
* err1 *
* </error> *
* AFTER *
* <error>err2</error> * <error>err3</error> *
";

            var errRegex = "error";

            var ops = SplitLines(text)
                .Select(l => Operation.Echo(l, true))
                .Concat(new[]
                {
                    Operation.WriteFile(CreateOutputFileArtifact()),
                    Operation.Fail()
                });
            var pipBuilder = CreatePipBuilder(ops);
            pipBuilder.ErrorRegex = new RegexDescriptor(StringId.Create(Context.StringTable, errRegex), RegexOptions.None);
            pipBuilder.EnableMultiLineErrorScanning = false;

            Process pip = SchedulePipBuilder(pipBuilder).Process;
            var runResult = RunScheduler();
            runResult.AssertFailure();

            AssertErrorEventLogged(LogEventId.PipProcessError);
            string expectedPrintedError;
            var stdFilePathInLogDir = Path.Combine(runResult.Config.Logging.LogsDirectory.ToString(Context.PathTable), SandboxedProcessPipExecutor.StdOutputsDirNameInLog, pip.FormattedSemiStableHash, SandboxedProcessFile.StandardError.DefaultFileName());
            if (outputReportingMode == global::BuildXL.Utilities.Configuration.OutputReportingMode.TruncatedOutputOnError)
            {
                expectedPrintedError = @"
* <error> *
* </error> *
* <error>err2</error> * <error>err3</error> *
This message has been filtered by a regex. Please find the complete stdout/stderr in the following file(s) in the log directory.";

                XAssert.FileExists(stdFilePathInLogDir, $"StandardError file {stdFilePathInLogDir} should had been copied to log directory");
            }
            else
            {
                expectedPrintedError = @"
* <error> *
* </error> *
* <error>err2</error> * <error>err3</error> *
This message has been filtered by a regex. Please find the complete stdout/stderr in the following file(s) or in the DX0066 event in the log file.";

                XAssert.FileDoesNotExist(stdFilePathInLogDir, $"StandardError file {stdFilePathInLogDir} should had been copied to log directory");
            }

            XAssert.ArrayEqual(
                SplitLines(expectedPrintedError),
                m_loggedPipFailures.SelectMany(SplitLines).ToArray());

            // Rerun the pip and the std file will be copied to the log directory too
            runResult = RunScheduler();
            runResult.AssertFailure();

            string extension = Path.GetExtension(stdFilePathInLogDir);
            var basename = stdFilePathInLogDir.Substring(0, stdFilePathInLogDir.Length - extension.Length);
            stdFilePathInLogDir = $"{basename}.1{extension}";

            if (outputReportingMode == global::BuildXL.Utilities.Configuration.OutputReportingMode.TruncatedOutputOnError)
            {
                XAssert.FileExists(stdFilePathInLogDir, $"StandardError file {stdFilePathInLogDir} should had been copied to log directory");
            }
            else
            {
                XAssert.FileDoesNotExist(stdFilePathInLogDir, $"StandardError file {stdFilePathInLogDir} should had been copied to log directory");
            }

            AssertErrorEventLogged(LogEventId.PipProcessError);
        }

        private string[] SplitLines(string text)
        {
            return text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
