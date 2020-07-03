// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Ipc {
    export declare const qualifier: BuildXLSdk.DefaultQualifierWithNet472;

    @@public
    export const dll = BuildXLSdk.library({
        assemblyName: "BuildXL.Ipc",
        sources: globR(d`.`, "*.cs"),
        references: [
            $.dll,
            $.Storage.dll,
            ...BuildXLSdk.systemThreadingTasksDataflowPackageReference,
        ],
        internalsVisibleTo: [
            "Test.BuildXL.Ipc",
        ],
    });
}
