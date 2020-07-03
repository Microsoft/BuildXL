﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Threading.Tasks;
using NLog;
using NLog.Common;
using NLog.Targets;

#nullable enable

namespace BuildXL.Cache.Logging.External
{
    /// <summary>
    ///     Makes <see cref="AzureBlobStorageLog"/> available as an NLog target
    /// </summary>
    [Target("AzureBlobStorageLogTarget")]
    public sealed class AzureBlobStorageLogTarget : TargetWithLayoutHeaderAndFooter
    {
        private readonly AzureBlobStorageLog _log;

        /// <nodoc />
        public AzureBlobStorageLogTarget(AzureBlobStorageLog log)
        {
            _log = log;
            _log.OnFileOpen = WriteHeaderAsync;
            _log.OnFileClose = WriteFooterAsync;
        }

        private Task WriteHeaderAsync(StreamWriter streamWriter)
        {
            if (Header != null)
            {
                var line = Header.Render(LogEventInfo.CreateNullEvent());
                return streamWriter.WriteLineAsync(line);
            }

            return Task.CompletedTask;
        }

        private Task WriteFooterAsync(StreamWriter streamWriter)
        {
            if (Footer != null)
            {
                var line = Footer.Render(LogEventInfo.CreateNullEvent());
                return streamWriter.WriteLineAsync(line);
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override void Write(LogEventInfo logEvent)
        {
            _log.Write(Layout.Render(logEvent));
        }

        /// <inheritdoc />
        protected override void CloseTarget()
        {
            InternalLogger.Warn("Closing {0} target", nameof(AzureBlobStorageLogTarget));
            var result = _log.ShutdownAsync().Result;
            if (!result.Succeeded)
            {
                InternalLogger.Error(
                    result.Exception,
                    "Failed to shutdown {0} target appropriately. Error: {1}",
                    nameof(AzureBlobStorageLogTarget),
                    result.Diagnostics ?? result.ErrorMessage ?? string.Empty);
            }
            else
            {
                InternalLogger.Info("Closed {0} target successfully", nameof(AzureBlobStorageLogTarget));
            }
        }
    }
}
