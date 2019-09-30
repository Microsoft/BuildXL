// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

import {Artifact, Cmd, Tool, Transformer} from "Sdk.Transformers";

/**
 * Result of deploying to disk.
 */
@@public
export interface OnDiskDeployment {
    /** Input data that was used for deployment */
    deployedDefinition: Definition;

    /** Static (sealed) directory that contains all the deployed files */
    contents: StaticDirectory;

    /** Optional primary file, i.e. an executable or test dll */
    primaryFile?: File;

    /** Optional opaque directories robocopied/rsynced into this deployment */
    targetOpaques?: OpaqueDirectory[];
}

@@public
export interface OpaqueSubDirectory extends Deployable {
    opaque: OpaqueDirectory,
    subDirectory: RelativePath,
    deploy: FlattenForDeploymentFunction
}

/**
 * Arguments to fine tune how things are deployed to disk
 */
@@public
export interface DeployToDiskArguments {
    /** The deployment definition to lay out on disk */
    definition: Definition;

    /** The target location where the deployment definition should be deployed to. */
    targetDirectory: Directory;

    /** Optional primary file for the resulting deployment. i.e. executable or test file. */
    primaryFile?: PathFragment;

    /** Optional list of tags to tag the pips with. */
    tags?: string[];

    /** A set of options specific to the deployment. deployToDisk just dumbly passes it along to the flatten method of the Deployable interface. */
    deploymentOptions?: DeploymentOptions;
}

@@public
export function createDeployableOpaqueSubDirectory(opaque: OpaqueDirectory, sub: RelativePath): OpaqueSubDirectory {
    return <OpaqueSubDirectory> {
        opaque: opaque,
        subDirectory: sub,
        deploy: (
            item: Object, 
            targetFolder: RelativePath,
            handleDuplicateFile: HandleDuplicateFileDeployment, 
            result: FlattenedResult,
            deploymentOptions?: Object,
            provenance?: Diagnostics.Provenance) => 
        {
            const existingOpaque = result.flattenedOpaques.get(targetFolder);

            if (existingOpaque !== undefined) {
                if (!(existingOpaque[0] === opaque && existingOpaque[1] === sub)) {
                    Contract.fail(`Duplicate opaque directory. Can't deploy both '${existingOpaque[0].root}/${existingOpaque[1]}' and '${opaque.root}/${sub}' to '${targetFolder}'`);
                }

                return result;
            }
            else {
                // TODO: Validate if there is a flattenedFile already under this OpaqueDirectory. To implement this we'll need IsWithin on RelativePath
                return {
                    flattenedFiles: result.flattenedFiles,
                    flattenedOpaques: result.flattenedOpaques.add(targetFolder, [<OpaqueDirectory>opaque, sub]),
                    visitedItems: result.visitedItems.add(d`{opaque.root}/${sub}`),
                };
            }
        }
    };
}

@@public
export function copyFileFromOpaqueDirectory(source: Path, target: Path, sourceOpaqueDir: OpaqueDirectory): DerivedFile {
    const args: Transformer.ExecuteArguments = Context.getCurrentHost().os === "win"
        ? <Transformer.ExecuteArguments>{
            tool: {
                exe: f`${Context.getMount("Windows").path}/System32/cmd.exe`,
                dependsOnWindowsDirectories: true,
                description: "Copy File",
            },
            workingDirectory: d`${source.parent}`,
            arguments: [
                Cmd.argument("copy"),
                Cmd.argument("/Y"),
                Cmd.argument("/V"),
                Cmd.argument(Artifact.none(source)),
                Cmd.argument(Artifact.output(target))
            ],
            dependencies: [
                sourceOpaqueDir
            ]
        }
        : <Transformer.ExecuteArguments>{
            tool: {
                exe: f`/bin/cp`,
                description: "Copy File",
                dependsOnCurrentHostOSDirectories: true,
                prepareTempDirectory: true
            },
            workingDirectory: d`${source.parent}`,
            arguments: [
                Cmd.argument("-f"),
                Cmd.argument(Artifact.none(source)),
                Cmd.argument(Artifact.output(target))
            ],
            dependencies: [
                sourceOpaqueDir
            ]
        };

    const result = Transformer.execute(args);
    return result.getOutputFile(target);
}

/**
 * Based on the current platform schedules either a robocopy.exe or rsync pip to copy 'sourceDir' to 'targetDir'
 */
@@public
export function copyDirectory(sourceDir: Directory, targetDir: Directory, sourceDirDep: StaticDirectory): OpaqueDirectory {
    const args: Transformer.ExecuteArguments = Context.getCurrentHost().os === "win"
        ? <Transformer.ExecuteArguments>{
            tool: {
                exe: f`${Context.getMount("Windows").path}/System32/Robocopy.exe`,
                dependsOnWindowsDirectories: true,
                description: "Copy Directory",
            },
            workingDirectory: targetDir,
            successExitCodes: [
                0,
                1,
                2,
                4,
            ],
            arguments: [
                Cmd.argument(Artifact.none(sourceDir)),
                Cmd.argument(Artifact.none(targetDir)),
                Cmd.argument("*.*"),
                Cmd.argument("/MIR"), // Mirror the directory
                Cmd.argument("/NJH"), // No Job Header
                Cmd.argument("/NFL"), // No File list reducing stdout processing
                Cmd.argument("/NP"),  // Don't show per-file progress counter
                Cmd.argument("/MT"),  // Multi threaded
            ],
            dependencies: [
                sourceDirDep
            ],
            outputs: [
                { directory: targetDir, kind: "shared" }
            ]
        }
        : <Transformer.ExecuteArguments>{
            tool: {
                exe: f`/usr/bin/rsync`,
                description: "Copy Directory",
                dependsOnCurrentHostOSDirectories: true,
                prepareTempDirectory: true
            },
            workingDirectory: targetDir,
            arguments: [
                Cmd.argument("-arvh"),
                Cmd.argument(Cmd.join("", [ Artifact.none(sourceDir), '/' ])),
                Cmd.argument(Artifact.none(targetDir)),
                Cmd.argument("--delete"),
            ],
            dependencies: [
                sourceDirDep
            ],
            outputs: [
                { directory: targetDir, kind: "shared" }
            ]
        };

    const result = Transformer.execute(args);
    return result.getOutputDirectory(targetDir);
}

/**
 * Deploys a given deployment to disk
 */
@@public
export function deployToDisk(args: DeployToDiskArguments): OnDiskDeployment {
    let rootDir = args.targetDirectory || Context.getNewOutputDirectory("deployment");

    const flattened = flatten(args.definition, undefined, args.deploymentOptions);

    const targetFiles = flattened.flattenedFiles.forEach(tuple => {
        const relativeTarget = tuple[0];
        const data = tuple[1];

        const targetPath = rootDir.combine(relativeTarget);

        return Transformer.copyFile(data.file, targetPath, args.tags);
    });

    const targetOpaques = flattened.flattenedOpaques.toArray().map(tuple => {
        const relativeTarget = tuple[0];
        const opaque = tuple[1][0];
        const opaqueSub = tuple[1][1];

        const targetDir = d`${rootDir}/${relativeTarget}`;
        return copyDirectory(d`${opaque}/${opaqueSub}`, targetDir, opaque);
    });

    // TODO: We lack the ability to combine files and OpaqueDirectories into a new OpaqueDirectory (unless we write a single process that would do all the copies)
    // Therefore for now we'll just copy the opaques but don't make it part of the output StaticDirectory field contents;
    // we do, however, pass those additional opaque directories along (via the 'targetOpaques' property)
    // so the caller can appropriately take dependencies on them.
    const contents = Transformer.sealPartialDirectory(rootDir, targetFiles, args.tags);

    return {
        deployedDefinition: args.definition,
        contents: contents,
        primaryFile : args.primaryFile ? contents.getFile(args.primaryFile) : undefined,
        targetOpaques: targetOpaques
    };
}

/**
 * Creates a deployment from disk by globbing the tree and constructing a definition out of it.
 * @param sourceRoot - The root of where to start from to create the deployment
 * @param patternOrOptions - The optional pattern to pass to the glob function for files. Defaults to '*'
 * @param recursive - Optionally indicates if the deployment should be crated recursively. Defaults to true.
 *
 * Remarks: The overloaded argument is for backwards compatibility. The intent is to deprecated the explicit arguments in favor of a compound options field.
 */
@@public
export function createFromDisk(sourceRoot: Directory, patternOrOptions?: (string | CreateFromDiskOptions), recursive?: boolean) : Definition {

    // Handle overload pattern
    const options : CreateFromDiskOptions = typeof patternOrOptions === "string" ? undefined : patternOrOptions;

    // Pattern needs to check the overloaded argument. Pattern defaults to '*'
    const pattern : string = typeof patternOrOptions === "string"
        ? patternOrOptions
        : options !== undefined ? options.pattern : "*";

    // Recursive prefers the excplicit argument. Recursive defaults to true
    recursive = recursive || (options !== undefined ? options.recursive : true);

    // Skip any files under excluded directories
    if (options && options.excludeDirectories) {
        if (options.excludeDirectories.indexOf(sourceRoot) >= 0) {
            return {
                contents: [],
            };
        }
    }
    
    let content : DeployableItem[] = [];
    
    let files = glob(sourceRoot, pattern);
    if (options && options.excludeFiles) {
        files = files.filter(file => !options.excludeFiles.contains(file));
    }
    content = files;

    if (recursive) {
        let directories = globFolders(sourceRoot, "*");
        for (let directory of directories) {
            const nested = createFromDisk(directory, patternOrOptions, recursive);
            const nestedWithFolder = {
                subfolder: directory.name,
                contents: [
                    nested
                ]
            };

            content = content.push(nestedWithFolder);
        }
    }

    return {
        contents: content,
    };
}

@@public
export interface CreateFromDiskOptions {
    /** Which directories to exclude */
    excludeDirectories?: Directory[],

    /** Which files to exclude */
    excludeFiles?: Set<File>,

    /** Wildcard pattern to match in each directory. */
    pattern?: string,

    /** Whether to recurse into directories or not  */
    recursive?: boolean
}