// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;

namespace BuildXL.FrontEnd.Workspaces.Core
{
    /// <summary>
    /// Known resolver kinds.
    /// </summary>
    public static class KnownResolverKind
    {
        /// <nodoc/>
        public const string SourceResolverKind = "SourceResolver";

        /// <nodoc/>
        public const string DScriptResolverKind = "DScript";

        /// <nodoc/>
        public const string NugetResolverKind = "Nuget";

        /// <nodoc/>
        public const string DownloadResolverKind = "Download";

        /// <nodoc/>
        public const string MsBuildResolverKind = "MsBuild";

        /// <nodoc/>
        public const string RushResolverKind = "Rush";

        /// <nodoc/>
        public const string YarnResolverKind = "Yarn";

        /// <nodoc/>
        public const string NinjaResolverKind = "Ninja";

        /// <nodoc/>
        public const string CMakeResolverKind = "CMake";

        /// <nodoc />
        public static readonly string DefaultSourceResolverKind = "DefaultSourceResolver";

        /// <nodoc />
        public static string[] KnownResolvers { get; } =
            {SourceResolverKind, DScriptResolverKind, NugetResolverKind, DefaultSourceResolverKind, DownloadResolverKind, MsBuildResolverKind, NinjaResolverKind, YarnResolverKind};

        /// <summary>
        /// Returns whether a given string is a valid resolver kind.
        /// </summary>
        [Pure]
        public static bool IsValid(string value)
        {
            return
                value == DefaultSourceResolverKind ||
                value == SourceResolverKind ||
                value == DScriptResolverKind ||
                value == NugetResolverKind ||
                value == DownloadResolverKind ||
                value == MsBuildResolverKind ||
                value == NinjaResolverKind ||
                value == CMakeResolverKind ||
                value == RushResolverKind ||
                value == YarnResolverKind;
        }
    }
}
