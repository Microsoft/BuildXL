// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import * as Managed from "Sdk.Managed";

export declare const qualifier: {
    configuration: "debug" | "release";
    targetFramework: "netcoreapp3.1" | "netstandard2.0" | "net462" | "net472";
    targetRuntime: "win-x64" | "osx-x64" | "linux-x64";
};

/** Configures which asserts should be checked at runtime. */
@@public
export const enum ContractsLevel {
    /** All assertions are disabled. */
    disabled = 0,
    /** Preconditions are enabled. */
    requires = 1 << 1,
    /** Postconditions are enabled. Currently not supported. */
    ensures = 1 << 2,
    /** Invariantes are enabled. */
    invariants = 1 << 3,
    /** Assertions (Contract.Assert and Contract.Assume) are enabled. */
    assertions = 1 << 4,

    // This is not valid today. Need to use a const value instead.
    // full = requires | ensures | invariants | assertions
}

export namespace ContractLevel {
    // Today we can't declare enum members with anything except numeric literals.
    // So we need to use a special namespace for common assertion levels.
    @public
    export const full: ContractsLevel = ContractsLevel.requires | ContractsLevel.ensures | ContractsLevel.invariants | ContractsLevel.assertions;
}

@@public
export function withRuntimeContracts(args: Managed.Arguments, contractsLevel?: ContractsLevel) : Managed.Arguments {
    const isDebug = qualifier.configuration === 'debug';

    return args.merge<Managed.Arguments>({
        defineConstants: getContractsSymbols(contractsLevel || ContractLevel.full, isDebug),
        references: [
            importFrom("RuntimeContracts").pkg,
        ],
        tools: {
            csc: {
                analyzers: [
                    ...dlls(importFrom("RuntimeContracts.Analyzer").Contents.all)
                ]
            },
        }});
}

export function getContractsSymbols(level: ContractsLevel, enableContractsQuantifiers?: boolean): string[] {
    let result: string[] = [];

    if (hasFlag(level, ContractsLevel.requires)) {
        result = result.push("CONTRACTS_LIGHT_PRECONDITIONS");
    }

    if (hasFlag(level, ContractsLevel.ensures)) {
        // Postconditions are not supported yet.
    }

    if (hasFlag(level, ContractsLevel.invariants)) {
        result = result.push("CONTRACTS_LIGHT_INVARIANTS");
    }

    if (hasFlag(level, ContractsLevel.assertions)) {
        result = result.push("CONTRACTS_LIGHT_ASSERTS");
    }

    if (enableContractsQuantifiers) {
        result = result.push("CONTRACTS_LIGHT_QUANTIFIERS");
    }

    return result;
}

/** Returns analyzers dll for RuntimeContracts nuget package. */
export function getAnalyzers() : Managed.Binary[] {
    return dlls(importFrom("RuntimeContracts.Analyzer").Contents.all);
}

function dlls(contents: StaticDirectory): Managed.Binary[] {
    // Getting dlls from the 'cs' folder.
    // This is not 100% safe but good enough.

    return contents
        .getContent()
        .filter(file => file.extension === a`.dll` && file.parent.name === a`cs`)
        .map(file => Managed.Factory.createBinary(contents, file));
}

function hasFlag(level: ContractsLevel, c: ContractsLevel): boolean {
    return ((level & c) === c);
}
