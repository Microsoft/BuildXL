// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Xml.Linq;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.FrontEnd.Nuget;
using BuildXL.FrontEnd.Sdk;

using static Test.BuildXL.TestUtilities.Xunit.XunitBuildXLTest;

namespace Test.BuildXL.FrontEnd.Nuget
{
    internal class PackageGenerator
    {
        private readonly FrontEndContext m_context;
        private readonly NugetFrameworkMonikers m_monikers;

        public PackageGenerator(FrontEndContext context, NugetFrameworkMonikers monikers)
        {
            m_context = context;
            m_monikers = monikers;
        }

        public NugetAnalyzedPackage AnalyzePackage(string xml, Dictionary<string, INugetPackage> packagesOnConfig, params string[] relativePaths)
        {
            var nugetPackage = new NugetPackage() { Id = "TestPkg", Version = "1.999" };

            var paths = new List<RelativePath>();
            paths.Add(RelativePath.Create(m_context.StringTable, nugetPackage.Id + ".nuspec"));
            foreach (var relativePath in relativePaths)
            {
                paths.Add(RelativePath.Create(m_context.StringTable, relativePath));
            }

            var packageOnDisk = new PackageOnDisk(
                m_context.PathTable,
                nugetPackage,
                PackageDownloadResult.FromRemote(
                    new PackageIdentity("nuget", nugetPackage.Id, nugetPackage.Version, nugetPackage.Alias),
                    AbsolutePath.Create(m_context.PathTable, A("X", "Pkgs", "TestPkg", "1.999", "TestPkg.nuspec")),
                    paths,
                    "testPackageHash"));

            return NugetAnalyzedPackage.TryAnalyzeNugetPackage(m_context, m_monikers, XDocument.Parse(xml), packageOnDisk, packagesOnConfig, false);
        }

        public NugetAnalyzedPackage AnalyzePackageStub(Dictionary<string, INugetPackage> packagesOnConfig)
        {
            var nugetPackage = new NugetPackage() { Id = "TestPkgStub", Version = "1.999" };
            var packageOnDisk = new PackageOnDisk(
                m_context.PathTable,
                nugetPackage,
                PackageDownloadResult.EmptyStub(
                    "testPackageStubHash",
                    new PackageIdentity("nuget", nugetPackage.Id, nugetPackage.Version, nugetPackage.Alias),
                    packageFolder: AbsolutePath.Create(m_context.PathTable, X("/X/Pkgs/TestPkgStub/1.999"))));

            return NugetAnalyzedPackage.TryAnalyzeNugetPackage(m_context, m_monikers, nuSpec: null, packageOnDisk, packagesOnConfig, false);
        }
    }
}
