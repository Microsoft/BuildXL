// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace VstsTest {
    @@public
    export const dll = !BuildXLSdk.Flags.isVstsArtifactsEnabled || BuildXLSdk.isDotNetCoreBuild ? undefined : BuildXLSdk.cacheTest({
        assemblyName: "BuildXL.Cache.MemoizationStore.Vsts.Test",
        sources: globR(d`.`,"*.cs"),
        appConfig: f`App.Config`,
        skipTestRun: BuildXLSdk.restrictTestRunToSomeQualifiers,
        references: [
            ...addIfLazy(BuildXLSdk.isFullFramework, () => [
                NetFx.System.Xml.dll,
                NetFx.System.Xml.Linq.dll,
            ]),
            ContentStore.Distributed.dll,
            ContentStore.DistributedTest.dll,
            ContentStore.UtilitiesCore.dll,
            ContentStore.Hashing.dll,
            ContentStore.Interfaces.dll,
            ContentStore.InterfacesTest.dll,
            ContentStore.Library.dll,
            ContentStore.Test.dll,
            ContentStore.Vsts.dll,
            Distributed.dll,
            InterfacesTest.dll,
            Interfaces.dll,
            Library.dll,
            VstsInterfaces.dll,
            Vsts.dll,

            importFrom("Newtonsoft.Json").pkg,
            ...importFrom("BuildXL.Cache.ContentStore").redisPackages,
            importFrom("Microsoft.VisualStudio.Services.Client").pkg,
            ...BuildXLSdk.visualStudioServicesArtifactServicesWorkaround,
            ...BuildXLSdk.fluentAssertionsWorkaround,
        ],
        runtimeContent: [
            {
                subfolder: r`redisServer`,
                contents: [
                    ...BuildXLSdk.isTargetRuntimeOsx 
                        ? importFrom("Redis-osx-x64").Contents.all.contents 
                        : importFrom("Redis-64").Contents.all.contents,
                ]
            },
            ...addIf(BuildXLSdk.isFullFramework,
                importFrom("Microsoft.VisualStudio.Services.BlobStore.Client").pkg
            ),
        ],
    });
}
