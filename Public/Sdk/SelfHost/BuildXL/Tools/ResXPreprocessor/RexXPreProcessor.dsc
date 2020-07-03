// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Transformer} from "Sdk.Transformers";
import * as Managed from "Sdk.Managed";
import * as BuildXLSdk from "Sdk.BuildXL";

export declare const qualifier: Managed.TargetFrameworks.MachineQualifier.Current;

const exe = BuildXLSdk.executable({
    assemblyName: "ResXPreProcessor",
    sources: globR(d`.`,"*.cs"),
});

const tool = Managed.deployManagedTool({
    tool: exe,
    description: "ResXPreProcessor",
    options: {
        prepareTempDirectory: true,
    }
});

@@public
export function preProcess(args: Arguments) : Result {
    const outputDir = Context.getNewOutputDirectory('resXPP');
    const outputFile = p`${outputDir}/${args.resX.name}`;

    let arguments = [
        Cmd.argument(Artifact.input(args.resX)),
        Cmd.argument(Artifact.output(outputFile)),
        Cmd.options("/d:", args.defines.map(kv => `${kv.key}=${kv.value}`)),
    ];

    let result = Transformer.execute({
        tool: tool,
        arguments: arguments,
        workingDirectory: outputDir,
    });

    return {
        resX: result.getOutputFile(outputFile),
    };
}

@@public
export interface Arguments {
    resX: File,
    defines: { key: string, value: string}[]
}

export interface Result {
    resX: File,
}