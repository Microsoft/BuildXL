// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as DetoursServices from "BuildXL.Sandbox.Windows";
import * as Managed from "Sdk.Managed";
import * as Deployment from "Sdk.Deployment";

// Tests are skipped because they call BuildXL executable directly and expect server mode to launch.
// Using BuildXL to execute another BuildXL executable with server mode will not work because the outer BuildXL will disallow any child process breakaway.
namespace IntegrationTest.BuildXL.Executable {

    export declare const qualifier : {
        configuration: "debug" | "release",
        targetFramework: "netcoreapp3.1",
        targetRuntime: "win-x64"
    };

    const exampleMountPath = Context.getMount("Example").path;
    @@public
    export const dll = BuildXLSdk.test({
        runTestArgs: {
            // These tests require Detours to run itself, so we won't detour the test runner process itself
            unsafeTestRunArguments: {
                runWithUntrackedDependencies: true
            },
        },
        assemblyName: "IntegrationTest.BuildXL.Executable",
        sources: globR(d`.`, "*.cs").filter(file =>
        {
            // For DotNetCore only include tests classes that begin with "DotNetCore"
            const fileName = file.name.toString();
            if (!fileName.endsWith("Tests.cs"))
            {
                return true;
            }

            return BuildXLSdk.isDotNetCoreBuild ===  fileName.startsWith("DotNetCore");
        }),
        skipTestRun: true,
        references: [
            Main.exe,
            importFrom("BuildXL.Engine").Cache.dll,
            importFrom("BuildXL.Tools").Execution.Analyzer.exe,
            importFrom("BuildXL.Utilities").dll,
            importFrom("BuildXL.Utilities").Configuration.dll,
            importFrom("BuildXL.Utilities").Native.dll,
            importFrom("BuildXL.Utilities.UnitTests").StorageTestUtilities.dll,
            importFrom("Newtonsoft.Json").pkg,
        ],
        runtimeContent: [
            serverDeployment,
            {
                subfolder: "TestBuild",
                contents: [
                    BuildXLSdk.isDotNetCoreBuild
                        ? Deployment.createFromDisk(d`${exampleMountPath}\DotNetCoreBuild`, {excludeDirectories: [d`${exampleMountPath}\DotNetCoreBuild\Out`]}, true /*recursive*/)
                        : Deployment.createFromDisk(d`${exampleMountPath}\HelloWorld`, {excludeDirectories: [d`${exampleMountPath}\HelloWorld\Out`]}, true /*recursive*/),
                ],
            },
        ],
    });

    const testsDeployment = dll.testDeployment.deployedDefinition;
}