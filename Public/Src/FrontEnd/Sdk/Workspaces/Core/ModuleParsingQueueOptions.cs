// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks.Dataflow;

namespace BuildXL.FrontEnd.Workspaces.Core
{
    /// <summary>
    /// Provides options for configuring a <see cref="ModuleParsingQueue"/>
    /// </summary>
    public sealed class ModuleParsingQueueOptions : ExecutionDataflowBlockOptions
    {
        /// <summary>
        /// When true, cancels parsing as soon as there is any failure.
        /// When false, tries to complete parsing as much as possible.
        /// </summary>
        public bool CancelOnFirstFailure { get; set; }
    }
}
