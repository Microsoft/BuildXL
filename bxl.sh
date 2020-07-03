#!/bin/bash

set -e

MY_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
source "$MY_DIR/Public/Src/Sandbox/MacOs/scripts/env.sh"

declare arg_Positional=()
declare arg_DeployDev=""
declare arg_DeployDevRelease=""
declare arg_UseDev=""
declare arg_Minimal=""
declare arg_Internal=""
declare arg_Cgmanifest=""

if [[ "${OSTYPE}" == "linux-gnu" ]]; then
    readonly HostQualifier=Linux
    readonly DeploymentFolder=linux-x64
elif [[ "${OSTYPE}" == "darwin"* ]]; then
    readonly HostQualifier=DotNetCoreMac
    readonly DeploymentFolder=osx-x64
else
    print_error "Operating system not supported: ${OSTYPE}"
    exit 1
fi

function callNuget() {
    if [[ "${OSTYPE}" == "linux-gnu" ]]; then
        nuget "$@"
    elif [[ "${OSTYPE}" == "darwin"* ]]; then
        $MONO_HOME/mono Shared/Tools/NuGet.exe "$@"
    else
        print_error "Operating system not supported: ${OSTYPE}"
        return 1
    fi
}

function findMono() {
    local monoLocation=$(which mono)
    if [[ -z $monoLocation ]]; then
        print_error "Did not find Mono. Please ensure mono is installed per: https://www.mono-project.com/docs/getting-started/install/ and is accessable in your PATH"
        return 1
    else
        export MONO_HOME="$(dirname "$monoLocation")"
    fi
}

function getLkg() {
    local LKG_FILE="BuildXLLkgVersionPublic.cmd"

    if [[ -n "$arg_Internal" ]]; then
        local LKG_FILE="BuildXLLkgVersion.cmd"
    fi

    local BUILDXL_LKG_VERSION=$(grep "BUILDXL_LKG_VERSION" "$MY_DIR/Shared/Scripts/$LKG_FILE" | cut -d= -f2 | tr -d '\r')
    local BUILDXL_LKG_NAME=$(grep "BUILDXL_LKG_NAME" "$MY_DIR/Shared/Scripts/$LKG_FILE" | cut -d= -f2 | perl -pe 's/(net472|win-x64)/'${DeploymentFolder}'/g' | tr -d '\r')
    local BUILDXL_LKG_FEED_1=$(grep "BUILDXL_LKG_FEED_1" "$MY_DIR/Shared/Scripts/$LKG_FILE" | cut -d= -f2 | tr -d '\r')

    print_info "Nuget Feed: $BUILDXL_LKG_FEED_1"
    print_info "Getting package: $BUILDXL_LKG_NAME.$BUILDXL_LKG_VERSION"

    local _BUILDXL_BOOTSTRAP_OUT="$MY_DIR/Out/BootStrap"
    callNuget install -OutputDirectory "$_BUILDXL_BOOTSTRAP_OUT" -Source $BUILDXL_LKG_FEED_1 $BUILDXL_LKG_NAME -Version $BUILDXL_LKG_VERSION
    export BUILDXL_BIN="$_BUILDXL_BOOTSTRAP_OUT/$BUILDXL_LKG_NAME.$BUILDXL_LKG_VERSION"
}

function setMinimal() {
    arg_Positional+=(/q:Debug${HostQualifier} "/f:output='$MY_DIR/Out/bin/debug/${DeploymentFolder}/*'")
}

function setInternal() {
    arg_Positional+=("/p:[Sdk.BuildXL]microsoftInternal=1")
    arg_Positional+=("/remoteTelemetry+")

    for arg in "$@"
    do
        to_lower=`printf '%s\n' "$arg" | awk '{ print tolower($0) }'`
        if [[ " $to_lower " == *"endpointsecurity"* ]]; then
            return
        fi
    done
}

function compileWithBxl() {
    local args=(
        --config "$MY_DIR/config.dsc"
        /fancyConsoleMaxStatusPips:10
        /exp:LazySODeletion
        /nowarn:11319 # DX11319: nuget version mismatch
        "$@"
    )

    if [[ -z "${VSTS_BUILDXL_BIN}" ]]; then
        "$BUILDXL_BIN/bxl.sh" "${args[@]}"
    else
        # Currently only used on VSTS CI to allow for custom BuildXL binary execution
        "$VSTS_BUILDXL_BIN/bxl.sh" "${args[@]}"
    fi
}

function printHelp() {
    echo "${BASH_SOURCE[0]} [--deploy-dev[-release]] [--use-dev] [--minimal] [--internal] [--shared-comp] [--cgmanifest] [--vs] [--test-method <full-test-method-name>] [--test-class <full-test-class-name>] <other-arguments>"
}

function parseArgs() {
    arg_Positional=()
    while [[ $# -gt 0 ]]; do
        cmd="$1"
        case $cmd in
        --help | -h)
            printHelp
            shift
            return 1
            ;;
        --deploy-dev)
            arg_DeployDev="1"
            shift
            ;;
        --deploy-dev-release)
            arg_DeployDevRelease="1"
            shift
            ;;
        --use-dev)
            arg_UseDev="1"
            shift
            ;;
        --minimal)
            arg_Minimal="1"
            shift
            ;;
        --internal)
            arg_Internal="1"
            shift
            ;;
        --cgmanifest)
            arg_Cgmanifest="1"
            shift
            ;;
        --test-class)
            arg_Positional+=("/p:[UnitTest]Filter.testClass=$2")
            shift
            shift
            ;;
        --test-method)
            arg_Positional+=("/p:[UnitTest]Filter.testMethod=$2")
            shift
            shift
            ;;
        --shared-comp)
            arg_Positional+=("/p:[Sdk.BuildXL]useManagedSharedCompilation=1")
            shift
            ;;
        --vs)
            arg_Positional+=(
                "/vs"
                "/vsTargetFramework:netcoreapp3.0"
                "/vsTargetFramework:netcoreapp3.1"
                "/vsTargetFramework:netstandard2.0"
                "/vsTargetFramework:netstandard2.1")
            shift
            ;;
        *)
            arg_Positional+=("$1")
            shift
            ;;
        esac
    done
}

function deployBxl { # (fromDir, toDir)
    local fromDir="$1"
    local toDir="$2"

    mkdir -p "$toDir"
    /usr/bin/rsync -arhq "$fromDir/" "$toDir" --delete
    print_info "Successfully deployed developer build from $fromDir to: $toDir; use it with the '--use-dev' flag now."
}

# allow this script to be sourced, in which case we shouldn't execute anything
if [[ "$0" != "${BASH_SOURCE[0]}" ]]; then 
    return 0
fi

# Make sure we are running in our own working directory
pushd "$MY_DIR"

parseArgs "$@"

findMono

if [[ -n "$arg_DeployDev" || -n "$arg_Minimal" ]]; then
    setMinimal
fi

if [[ -n "$arg_DeployDevRelease" ]]; then
    arg_Positional+=(/q:Release${HostQualifier} "/f:output='$MY_DIR/Out/bin/release/${DeploymentFolder}/*'")
fi

if [[ -n "$arg_Internal" ]]; then
    setInternal $@
fi

if [[ -n "$arg_Cgmanifest" ]]; then
    arg_Positional+=(/generateCgManifestForNugets:"${MY_DIR}/cg/nuget/cgmanifest.json")
fi

if [[ -n "$arg_UseDev" ]]; then
    if [[ ! -f $MY_DIR/Out/Selfhost/Dev/bxl ]]; then
        print_error "Error: Could not find the dev deployment. Make sure you build with --deploy-dev first."
        exit 1
    fi

    export BUILDXL_BIN=$MY_DIR/Out/Selfhost/Dev
else
    getLkg
fi

compileWithBxl ${arg_Positional[@]}

if [[ -n "$arg_DeployDev" ]]; then
    deployBxl "$MY_DIR/Out/Bin/debug/${DeploymentFolder}" "$MY_DIR/Out/Selfhost/Dev"
fi

if [[ -n "$arg_DeployDevRelease" ]]; then
    deployBxl "$MY_DIR/Out/Bin/release/${DeploymentFolder}" "$MY_DIR/Out/Selfhost/Dev"
fi

popd