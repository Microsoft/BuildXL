﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BuildXL.Utilities.VmCommandProxy
{
    /// <summary>
    /// VM initializer.
    /// </summary>
    /// <remarks>
    /// Currently, VM initialization requires user name and password, and this initialization is considered
    /// temporary due to security leak. The CB team is addressing this issue.
    /// </remarks>
    public class VmInitializer
    {
        /// <summary>
        /// Timeout (in minute) for VM initialization.
        /// </summary>
        /// <remarks>
        /// According to CB team, VM initialization roughly takes 2-3 minutes.
        /// </remarks>
        private const int InitVmTimeoutInMinute = 10;

        /// <summary>
        /// Path to VmCommandProxy executable.
        /// </summary>
        public string VmCommandProxy { get; }

        /// <summary>
        /// Lazy VM initialization.
        /// </summary>
        public readonly Lazy<Task> LazyInitVmAsync;

        private readonly Action<string> m_logStartInit;
        private readonly Action<string> m_logEndInit;
        private readonly Action<string> m_logInitExecution;

        private readonly (string drive, string path)? m_subst;

        private readonly string m_workingDirectory;

        /// <summary>
        /// Creates an instance of <see cref="VmInitializer"/> from build engine.
        /// </summary>
        public static VmInitializer CreateFromEngine(
            string buildEngineDirectory,
            string initializationDirectory,
            string vmCommandProxyAlternate = null,
            (string drive, string path)? subst = null,
            Action<string> logStartInit = null,
            Action<string> logEndInit = null,
            Action<string> logInitExecution = null)
        {
            // VM command proxy will no longer be released along with BuildXL's release. In CB, BuildXL will use
            // VM command proxy that can be found through BUILDXL_VMCOMMANDPROXY_PATH environment variable.
            //
            // Here, prefer VM command proxy that comes with the build engine for two reasons:
            // - Unit tests use a mock version that comes with the deployment.
            // - As an escape hatch when we want to test a new VM command proxy without having to wait for CB deployment.
            string vmCommandProxy = Path.Combine(buildEngineDirectory, VmExecutable.DefaultRelativePath);

            if (!File.Exists(vmCommandProxy) && !string.IsNullOrWhiteSpace(vmCommandProxyAlternate))
            {
                // If engine does not have VM command proxy, then use the alternate one if properly specified.
                vmCommandProxy = vmCommandProxyAlternate;
            }

            return new VmInitializer(
                vmCommandProxy,
                Path.Combine(initializationDirectory, nameof(VmInitializer)),
                subst,
                logStartInit,
                logEndInit,
                logInitExecution);
        }

        /// <summary>
        /// Creates an instance of <see cref="VmInitializer"/>.
        /// </summary>
        private VmInitializer(
            string vmCommandProxy,
            string workingDirectory,
            (string drive, string path)? subst = null,
            Action<string> logStartInit = null,
            Action<string> logEndInit = null,
            Action<string> logInitExecution = null)
        {
            VmCommandProxy = vmCommandProxy;
            m_workingDirectory = workingDirectory;
            LazyInitVmAsync = new Lazy<Task>(() => InitVmAsync(), true);
            m_subst = subst;
            m_logStartInit = logStartInit;
            m_logEndInit = logEndInit;
            m_logInitExecution = logInitExecution;
        }

        private async Task InitVmAsync()
        {
            // (0) Prepare working directory.
            PrepareWorkingDirectory();

            // (1) Create and serialize input for InitializeVM command.
            var inputPath = Path.Combine(m_workingDirectory, nameof(VmCommands.InitializeVm) + ".json");
            var input = new InitializeVmRequest();
            if (m_subst.HasValue
                && !string.IsNullOrEmpty(m_subst.Value.drive)
                && !string.IsNullOrEmpty(m_subst.Value.path))
            {
                input.SubstDrive = m_subst.Value.drive;
                input.SubstPath = m_subst.Value.path;
            }

            VmSerializer.SerializeToFile(inputPath, input);

            // (2) Create a process to execute VmCommandProxy.
            string arguments = $"{VmCommands.InitializeVm} /{VmCommands.Params.InputJsonFile}:\"{inputPath}\"";
            var process = CreateVmCommandProxyProcess(arguments);

            m_logStartInit?.Invoke($"{VmCommandProxy} {arguments}");

            var stdOutForStartBuild = new StringBuilder();
            var stdErrForStartBuild = new StringBuilder();

            string provenance = $"[{nameof(VmInitializer)}]";

            // (3) Run VmCommandProxy to start build.
            using (var executor = new AsyncProcessExecutor(
                process,
                TimeSpan.FromMinutes(InitVmTimeoutInMinute),
                line => { if (line != null) { stdOutForStartBuild.AppendLine(line); } },
                line => { if (line != null) { stdErrForStartBuild.AppendLine(line); } },
                provenance: provenance,
                logger: message => m_logInitExecution?.Invoke(message)))
            {
                executor.Start();
                await executor.WaitForExitAsync();
                await executor.WaitForStdOutAndStdErrAsync();

                string stdOut = $"{Environment.NewLine}StdOut:{Environment.NewLine}{stdOutForStartBuild.ToString()}";
                string stdErr = $"{Environment.NewLine}StdErr:{Environment.NewLine}{stdErrForStartBuild.ToString()}";

                if (executor.Process.ExitCode != 0)
                {
                    throw new BuildXLException($"Failed to init VM '{VmCommandProxy} {arguments}', with exit code {executor.Process.ExitCode}{stdOut}{stdErr}");
                }

                m_logEndInit?.Invoke($"Exit code {executor.Process.ExitCode}{stdOut}{stdErr}");
            }
        }

        private Process CreateVmCommandProxyProcess(string arguments)
        {
            return new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = VmCommandProxy,
                    Arguments = arguments,
                    WorkingDirectory = m_workingDirectory,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                },

                EnableRaisingEvents = true
            };
        }

        private void PrepareWorkingDirectory()
        {
            if (Directory.Exists(m_workingDirectory))
            {
                Directory.Delete(m_workingDirectory, true);
            }

            Directory.CreateDirectory(m_workingDirectory);
        }
    }
}
