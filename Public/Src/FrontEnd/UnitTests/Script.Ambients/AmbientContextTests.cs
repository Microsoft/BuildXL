// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.DScript.Ast.Interpretation
{
    public class AmbientContextTests : DsTest
    {
        public AmbientContextTests(ITestOutputHelper output) 
            : base(output)
        {
        }

        [Fact]
        public void TestOutputDirectoryIsDifferentWhenClashingOnCasing()
        {
            var spec = @"
export const xx = Context.getNewOutputDirectory(""hello"");
export const xX = Context.getNewOutputDirectory(""hello"");

export const areEqual = xx === xX;

";
            var result = Build()
                .AddSpec("spec.dsc", spec)
                .RootSpec("spec.dsc")
                .EvaluateExpressionWithNoErrors("areEqual");

            Assert.Equal(false, result);
        }

        [Fact]
        public void TestLastActiveUseName()
        {
            var spec = @"
export const x = Context.getLastActiveUseName();

namespace A {
    export const x = Context.getLastActiveUseName();
}

namespace A.B.C {
    export const x = Context.getLastActiveUseName();
}
";
            var result = Build()
                .AddSpec("spec.dsc", spec)
                .RootSpec("spec.dsc")
                .EvaluateExpressionsWithNoErrors("x", "A.x", "A.B.C.x");

            Assert.Equal("x", result["x"]);
            Assert.Equal("A.x", result["A.x"]);
            Assert.Equal("A.B.C.x", result["A.B.C.x"]);
        }

        [Fact]
        public void TestLastActiveUseNamespace()
        {
            var spec = @"
export const x = Context.getLastActiveUseNamespace();

namespace A {
    export const x = Context.getLastActiveUseNamespace();
}

namespace A.B.C {
    export const x = Context.getLastActiveUseNamespace();
}
";
            var result = Build()
                .AddSpec("spec.dsc", spec)
                .RootSpec("spec.dsc")
                .EvaluateExpressionsWithNoErrors("x", "A.x", "A.B.C.x");

            Assert.Equal("{Invalid}", result["x"]);
            Assert.Equal("A", result["A.x"]);
            Assert.Equal("A.B.C", result["A.B.C.x"]);
        }

        [Fact]
        public void TestMountInConfig()
        {
            var config = @"
config({mounts:[{name: a`TestMount`, path: d`${Context.getMount('LogsDirectory').path}`}]});
";
            var spec = @"
export const x = Context.getMount('TestMount').path;
";
            var result = Build()
                .Configuration(config)
                .AddSpec("spec.dsc", spec)
                .RootSpec("spec.dsc")
                .Evaluate("x");

            Assert.Equal(result.Configuration.Logging.RedirectedLogsDirectory, result.Values[0]);
        }
    }
}
