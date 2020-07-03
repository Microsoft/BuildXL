// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Transformer} from "Sdk.Transformers";
import * as Deployment from "Sdk.Deployment";
import * as Xml from "Sdk.Xml";

const TestRunDataXmlFileName = a`testRunData.xml`;
const TestRunDataElementName = "TestRunData";

/**
 * Compiles an assembly using some of the given test frameworks defaults,
 * deploys the assembly and its closure to a testRun folder and then
 * uses the test frameworks runtest function to execute the test.
 */
@@public
export function test(args: TestArguments) : TestResult {
    const testFramework = args.testFramework;
    if (!testFramework) {
        Contract.fail("You must specify a Testing framework. For exmple: 'importFrom(\"Sdk.Managed.Testing.XUnit\").framework' ");
    }

    if (testFramework.compileArguments) {
        args = testFramework.compileArguments(args);
    }

    // If there is additional content to deploy, ensure it is on the argumetns
    // to compile the test library so that the managed deduplication semantics will apply for runtime binatires
    if (testFramework.additionalRuntimeContent)
    {
        args = args.merge({
            runtimeContent: testFramework.additionalRuntimeContent(args)
        });
    }

    const assembly = library(args);

    // Deploy assemblies (with all dependencies) to special folder.
    const testDeployFolder = Context.getNewOutputDirectory("testRun");
    const testDeployment = Deployment.deployToDisk({
        definition: {
            contents: [
                assembly,
                generateTestDataXmlFile(args.runTestArgs) // can be undefined, but that's fine
            ],
        },
        targetDirectory: testDeployFolder,
        primaryFile: assembly.runtime.binary.name,
        deploymentOptions: args.deploymentOptions,
        // Tag is required by ide generator to generate the right csproj file.
        tags: [ "testDeployment" ],
        sealPartialWithoutScrubbing: 
            args.runTestArgs && 
            args.runTestArgs.unsafeTestRunArguments && 
            args.runTestArgs.unsafeTestRunArguments.doNotScrubTestDeployment
    });

    return assembly.merge<TestResult>(runTestOnly(
        args, 
        /* compileArguments: */ false,
        /* testDeployment:   */ testDeployment));
}

/**
 * Runs test only provided that the test deployment has been given.
 */
@@public
export function runTestOnly(args: TestArguments, compileArguments: boolean, testDeployment: Deployment.OnDiskDeployment) : TestResult
{
    let testFramework = args.testFramework;
    if (!testFramework) {
        Contract.fail("You must specify a Testing framework. For exmple: 'importFrom(\"Sdk.Managed.Testing.XUnit\").framework' ");
    }

    if (testFramework.compileArguments && compileArguments) {
        args = testFramework.compileArguments(args);
    }

    let testRunArgs = Object.merge(args.runTestArgs, {testDeployment: testDeployment});

    let testResults = [];

    if (!args.skipTestRun) {
        if (testRunArgs.parallelBucketCount) {
            for (let i = 0; i < testRunArgs.parallelBucketCount; i++) {
                let bucketTestRunArgs = testRunArgs.merge({
                    parallelBucketIndex: i
                });

                testResults = testResults.concat(testFramework.runTest(bucketTestRunArgs));
            }
        } else {
            testResults = testFramework.runTest(testRunArgs);
        }
    }

    return <TestResult>{
        testResults: testResults,
        testDeployment: testDeployment
    };
}

namespace TestHelpers {
    export declare const qualifier: {};

    @@public
    export const TestFilterHashIndexName = "[UnitTest]TestFilterHashIndex";
    export const TestFilterHashCountName = "[UnitTest]TestFilterHashCount";

    /** Merges test tool configuration into the given execute arguments. */
    @@public
    export function applyTestRunExecutionArgs(execArguments: Transformer.ExecuteArguments, args: TestRunArguments) : Transformer.ExecuteArguments {
        // Unit test runners often want fine control over how the process gets executed, so provide a way to override here.
        if (args.tools && args.tools.exec) {
            execArguments = args.tools.exec.merge(execArguments);
        }

        // Some unit test runners 'nest' or 'wrap' in themselves, so allow for that.
        if (args.tools && args.tools.wrapExec) {
            execArguments = args.tools.wrapExec(execArguments);
        }

        // Specify environment variables used by XUnit Assembly runner to filter tests from particular hash bucket
        // Tests are split into 'args.parallelBucketCount' buckets which are run in parallel
        if (args.parallelBucketIndex && args.parallelBucketCount)
        {
            execArguments = execArguments.merge<Transformer.ExecuteArguments>({
                environmentVariables: [
                { name: TestFilterHashIndexName, value: `${args.parallelBucketIndex}` },
                { name: TestFilterHashCountName, value: `${args.parallelBucketCount}` },
            ]
            });
        }

        return execArguments;
    }
}

function generateTestDataXmlFile(testRunArgs: TestRunArguments): File {
    if (!testRunArgs || !testRunArgs.testRunData)
    {
        return undefined;
    }
    const testRunData = testRunArgs.testRunData;
    const entries = testRunData
        .keys()
        .map(key => Xml.elem("Entry", 
            Xml.elem("Key", key), 
            Xml.elem("Value", ["", testRunData[key]]))
        );

    const doc = Xml.doc(
        Xml.elem(TestRunDataElementName,
            ...entries
        )
    );

    return Xml.write(p`${Context.getNewOutputDirectory("testRunData")}/${TestRunDataXmlFileName}`, doc);
}

@@public
export interface TestArguments extends Arguments {
    /**
     * Which test framework to use when testing
     */
    testFramework?: TestFramework;

    /**
     * Optional special flags for the testrunner
     */
    runTestArgs?: TestRunArguments;

    /**
     * Option you can use to disable running tests
     */
    skipTestRun?: boolean;
}

@@public
export interface TestFramework {
    /** 
     * Function that allows processing of the arguments
     * that are used for compilation
     */
    compileArguments<T extends Arguments>(T) : T;

    /**
     * In case additional files need to be deployed;
     */
    additionalRuntimeContent?<T extends Arguments>(T): Deployment.DeployableItem[];


    /** The function that runs the test resulting in the test report file */
    runTest<T extends TestRunArguments>(T) : File[];

    /** Name of test framework */
    name: string;
}

@@public
export interface TestResult extends Result {
}


@@public
export interface TestRunArguments {
    /**The Deployment under tests */
    testDeployment?: Deployment.OnDiskDeployment;

    /** 
     * Test run data that is accessible from the actual test logic. 
     * This data gets written to an xml file, its path passed as an environment and is accessible using a helper
     * class in the test code.
     */
    testRunData?: Object;

    /** Nested tool options */
    tools?: {
        /** 
         * Since many test runners need custom ways to run the test processes, 
         * this is an optional settings for executing the test processs to 
         * allow for overidding the process execution settings
         * */
        exec?: Transformer.ExecuteArgumentsComposible;

        /**
         * Some test frameworks might want to wrap other test runners
         */
        wrapExec?: (exec: Transformer.ExecuteArguments) => Transformer.ExecuteArguments;
    };
    
    /**
     * Allows running tests in various groups if the testrunner supports it
     */
    parallelGroups?: string[];

    /**
     * Allows splitting test in consistent random parallel groups
     */
    parallelBucketIndex?: number;

    /**
     * Allows splitting test in consistent random parallel groups
     */
    parallelBucketCount?: number;

    /**
     * The test groups to limit this run to.
     */
    limitGroups?: string[];

    /**
     * Allows skipping certain test groups if the testrunner supports it.
     */
    skipGroups?: string[];

    /** Untrack test directory. */ 
    untrackTestDirectory?: boolean;
    
    /**
     * Allows test runs to be tagged.
     */
    tags?: string[];

    /** Optionally override to increase the weight of test pips that require more machine resources */
    weight?: number;

    /** Privilege level required by this process to execute. */
    privilegeLevel?: "standard" | "admin";

    /** Unsafe arguments for running unit tests. */
    unsafeTestRunArguments?: UnsafeTestRunArguments;

    /** Disables code coverage. */
    disableCodeCoverage? : boolean;
}

@@public
export interface UnsafeTestRunArguments {
    /** Allow testing zero test cases. */
    allowForZeroTestCases?: boolean;
    
    /** Allow dependencies to go untracked. */
    runWithUntrackedDependencies?: boolean;

    /** When set, XUnit test framework is used for running admin tests irrespective of any other settings */
    forceXunitForAdminTests?: boolean;

    /** Blocks scrubbing of stale files under the test deployment. Useful when the test happens to create or lock files
     * under the deployment root
    */
    doNotScrubTestDeployment?: boolean;
}

@@public
export interface TestResult extends Result {
    /**
     * The test result files
     */
    testResults: File[];

    /**
     * The test deployment
     */
    testDeployment: Deployment.OnDiskDeployment;
}
