// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit.Abstractions;

namespace IntegrationTest.BuildXL.Scheduler.IncrementalSchedulingTests
{
    /// <summary>
    /// We test incremental scheduling in a separate class so that we can control the
    /// requirement of journal scanning (see: [TheoryIfSupported(requiresJournalScan: true)] in JunctionTests below).
    /// </summary>
    [Feature(Features.IncrementalScheduling)]
    [TestClassIfSupported(requiresJournalScan: true)]
    public class DirectoryTranslationJunctionSubstTests_IncrementalScheduling : DirectoryTranslationJunctionSubstTests
    {
        public DirectoryTranslationJunctionSubstTests_IncrementalScheduling(ITestOutputHelper output) : base(output)
        {
            Configuration.Schedule.IncrementalScheduling = true;
            Configuration.Schedule.SkipHashSourceFile = false;
        }
    }
}
