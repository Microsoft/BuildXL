// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";
import * as MSBuild from "Sdk.Selfhost.MSBuild";

namespace Test.Tool.MsBuildGraphBuilder {

    export declare const qualifier: BuildXLSdk.DefaultQualifierWithNet472;

    @@public
    export const dll = BuildXLSdk.test({
        assemblyName: "Test.Tool.ProjectGraphBuilder",
        sources: globR(d`.`, "*.cs"),
        references:[
            importFrom("BuildXL.Tools").MsBuildGraphBuilder.exe,
            importFrom("Microsoft.Build.Prediction").pkg,
            importFrom("Newtonsoft.Json").pkg,
            importFrom("BuildXL.FrontEnd").MsBuild.Serialization.dll,
            ...MSBuild.msbuildReferences,
        ],
        runtimeContent: [
            ...MSBuild.msbuildRuntimeContent,
            ...MSBuild.msbuildReferences,
        ]
    });
}
