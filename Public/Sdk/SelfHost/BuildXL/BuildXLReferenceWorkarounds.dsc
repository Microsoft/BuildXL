
/** Returns a list of nuget packages required to use Task/Task<T>/ValueTask<T>. */
@@public
export const tplPackages = isDotNetCoreApp ? [] : [importFrom("System.Threading.Tasks.Extensions").pkg];

@@public
export const fluentAssertionsWorkaround = [
    importFrom("FluentAssertions").pkg,
    importFrom("System.Configuration.ConfigurationManager").pkg,
];

/**
 * This is a workaround for visual studio artifact services not having AAD Auth in the netcore binaries.
 * We therefore on windows on .net core use the full framework net472 binaries and it happens to work by
 * accident since. Ideally the AS team ships a windows and a mac version of netstandard where the windows version
 * contains AAD and the mac not...
 */
@@public
export const visualStudioServicesArtifactServicesWorkaround = [
    qualifier.targetRuntime === "win-x64" 
        ? importFrom("Microsoft.VisualStudio.Services.ArtifactServices.Shared").withQualifier({targetFramework: "net472"}).pkg
        : importFrom("Microsoft.VisualStudio.Services.ArtifactServices.Shared").pkg,
    // Missing dependency since nuspec only declares dependencies specifically for net462. Make sure to include it here.
    qualifier.targetFramework === "netstandard2.0"
        ? importFrom("Microsoft.Azure.Storage.Common").withQualifier({targetFramework: "net472"}).pkg
        : importFrom("Microsoft.Azure.Storage.Common").pkg
];
