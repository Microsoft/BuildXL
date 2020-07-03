// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

const coreVersion = "3.1.0";
const coreVersionPreviousVersion = "2.2.0";
const pkgVersion = "4.3.0";
const pkgVersionNext = "4.7.0";

export const pkgs = [

    // .NET Core Dependencies
    { id: "Microsoft.NETCore.App.Ref", version: coreVersion },

    { id: "NETStandard.Library", version: "2.0.3", tfm: ".NETStandard2.0" },
    { id: "Microsoft.NETCore.Platforms", version: coreVersion },
    
    // .NET Core Self-Contained Deployment
    { id: "Microsoft.NETCore.DotNetHostResolver", version: coreVersion },
    { id: "Microsoft.NETCore.DotNetHostResolver", version: coreVersionPreviousVersion, alias: "Microsoft.NETCore.DotNetHostResolver.220" },

    { id: "Microsoft.NETCore.DotNetHostPolicy", version: coreVersion },

    { id: "Microsoft.NETCore.DotNetAppHost", version: coreVersion },
    { id: "Microsoft.NETCore.DotNetAppHost", version: coreVersionPreviousVersion, alias: "Microsoft.NETCore.DotNetAppHost.220" },

    // .NET Core win-x64 runtime deps
    { id: "Microsoft.NETCore.App.Host.win-x64", version: coreVersion, osSkip: [ "macOS", "unix" ] },
    { id: "Microsoft.NETCore.App.Runtime.win-x64", version: coreVersion, osSkip: [ "macOS", "unix" ] },
    { id: "runtime.win-x64.Microsoft.NETCore.DotNetHostResolver", version: coreVersion, osSkip: [ "macOS", "unix" ] },
    { id: "runtime.win-x64.Microsoft.NETCore.DotNetHostPolicy", version: coreVersion, osSkip: [ "macOS", "unix" ] },

    { id: "runtime.win-x64.Microsoft.NETCore.DotNetAppHost", version: coreVersionPreviousVersion, alias: "runtime.win-x64.Microsoft.NETCore.DotNetAppHost.220", osSkip: [ "macOS", "unix" ] },
    { id: "runtime.win-x64.Microsoft.NETCore.DotNetHostResolver", version: coreVersionPreviousVersion, alias: "runtime.win-x64.Microsoft.NETCore.DotNetHostResolver.220", osSkip: [ "macOS", "unix" ] },
    { id: "runtime.win-x64.Microsoft.NETCore.DotNetHostPolicy", version: coreVersionPreviousVersion, alias: "runtime.win-x64.Microsoft.NETCore.DotNetHostPolicy.220", osSkip: [ "macOS", "unix" ] },

    // .NET Core osx-x64 runtime deps
    { id: "Microsoft.NETCore.App.Host.osx-x64", version: coreVersion },
    { id: "Microsoft.NETCore.App.Runtime.osx-x64", version: coreVersion },
    { id: "runtime.osx-x64.Microsoft.NETCore.DotNetHostResolver", version: coreVersion },
    { id: "runtime.osx-x64.Microsoft.NETCore.DotNetHostPolicy", version: coreVersion },

    { id: "runtime.osx-x64.Microsoft.NETCore.DotNetAppHost", version: coreVersionPreviousVersion, alias: "runtime.osx-x64.Microsoft.NETCore.DotNetAppHost.220" },
    { id: "runtime.osx-x64.Microsoft.NETCore.DotNetHostResolver", version: coreVersionPreviousVersion, alias: "runtime.osx-x64.Microsoft.NETCore.DotNetHostResolver.220" },
    { id: "runtime.osx-x64.Microsoft.NETCore.DotNetHostPolicy", version: coreVersionPreviousVersion, alias: "runtime.osx-x64.Microsoft.NETCore.DotNetHostPolicy.220" },

    // .NET Core linux-x64 runtime deps
    { id: "Microsoft.NETCore.App.Runtime.linux-x64", version: coreVersion },
    { id: "Microsoft.NETCore.App.Host.linux-x64", version: coreVersion },
    { id: "runtime.linux-x64.Microsoft.NETCore.DotNetHostResolver", version: coreVersion },
    { id: "runtime.linux-x64.Microsoft.NETCore.DotNetHostPolicy", version: coreVersion },

    { id: "runtime.native.System", version: pkgVersion },
    { id: "runtime.win7-x64.runtime.native.System.Data.SqlClient.sni", version: pkgVersion, osSkip: [ "macOS", "unix" ] },
    { id: "runtime.win7-x86.runtime.native.System.Data.SqlClient.sni", version: pkgVersion, osSkip: [ "macOS", "unix" ] },
    { id: "runtime.native.System.Data.SqlClient.sni", version: pkgVersion },
    { id: "runtime.native.System.Net.Http", version: pkgVersion },
    { id: "runtime.native.System.IO.Compression", version: pkgVersion },
    { id: "runtime.native.System.Net.Security", version: pkgVersion },
    { id: "runtime.native.System.Security.Cryptography.Apple", version: pkgVersion },
    { id: "runtime.osx.10.10-x64.runtime.native.System.Security.Cryptography.Apple", version: pkgVersion },
    { id: "runtime.native.System.Security.Cryptography.OpenSsl", version: pkgVersion },
    { id: "runtime.debian.8-x64.runtime.native.System.Security.Cryptography.OpenSsl", version: pkgVersion },
    { id: "runtime.fedora.23-x64.runtime.native.System.Security.Cryptography.OpenSsl", version: pkgVersion },
    { id: "runtime.fedora.24-x64.runtime.native.System.Security.Cryptography.OpenSsl", version: pkgVersion },
    { id: "runtime.opensuse.13.2-x64.runtime.native.System.Security.Cryptography.OpenSsl", version: pkgVersion },
    { id: "runtime.opensuse.42.1-x64.runtime.native.System.Security.Cryptography.OpenSsl", version: pkgVersion },
    { id: "runtime.osx.10.10-x64.runtime.native.System.Security.Cryptography.OpenSsl", version: pkgVersion },
    { id: "runtime.rhel.7-x64.runtime.native.System.Security.Cryptography.OpenSsl", version: pkgVersion },
    { id: "runtime.ubuntu.14.04-x64.runtime.native.System.Security.Cryptography.OpenSsl", version: pkgVersion },
    { id: "runtime.ubuntu.16.04-x64.runtime.native.System.Security.Cryptography.OpenSsl", version: pkgVersion },
    { id: "runtime.ubuntu.16.10-x64.runtime.native.System.Security.Cryptography.OpenSsl", version: pkgVersion },

    // Packages
    { id: "Microsoft.CSharp", version: pkgVersion },
    { id: "Microsoft.Win32.Primitives", version: pkgVersion },
    { id: "Microsoft.Win32.Registry", version: pkgVersion },
    { id: "System.AppContext", version: pkgVersion },
    { id: "System.Collections", version: pkgVersion },
    { id: "System.Collections.Concurrent", version: pkgVersion },
    { id: "System.Collections.NonGeneric", version: pkgVersion },
    { id: "System.Collections.Specialized", version: pkgVersion },
    { id: "System.ComponentModel", version: pkgVersion },
    { id: "System.ComponentModel.Annotations", version: pkgVersion },
    { id: "System.ComponentModel.Composition", version: "4.5.0" },
    { id: "System.ComponentModel.EventBasedAsync", version: pkgVersion },
    { id: "System.ComponentModel.Primitives", version: pkgVersion },
    { id: "System.ComponentModel.TypeConverter", version: pkgVersion },
    { id: "System.Console", version: pkgVersion },
    { id: "System.Data.Common", version: pkgVersion },
    { id: "System.Data.SqlClient", version: pkgVersion },
    { id: "System.Diagnostics.Contracts", version: pkgVersion },
    { id: "System.Diagnostics.Debug", version: pkgVersion },
    { id: "System.Diagnostics.FileVersionInfo", version: pkgVersion },
    { id: "System.Diagnostics.Process", version: pkgVersion },
    { id: "System.Diagnostics.StackTrace", version: pkgVersion },
    { id: "System.Diagnostics.TextWriterTraceListener", version: pkgVersion },
    { id: "System.Diagnostics.Tools", version: pkgVersion },
    { id: "System.Diagnostics.TraceSource", version: pkgVersion },
    { id: "System.Diagnostics.Tracing", version: pkgVersion },
    { id: "System.Drawing.Primitives", version: pkgVersion },
    { id: "System.Dynamic.Runtime", version: pkgVersion },
    { id: "System.Globalization", version: pkgVersion },
    { id: "System.Globalization.Calendars", version: pkgVersion },
    { id: "System.Globalization.Extensions", version: pkgVersion },
    { id: "System.IO", version: pkgVersion },
    { id: "System.IO.Compression", version: pkgVersion },
    { id: "System.IO.Compression.ZipFile", version: pkgVersion },
    { id: "System.IO.FileSystem", version: pkgVersion },
    { id: "System.IO.FileSystem.DriveInfo", version: pkgVersion },
    { id: "System.IO.FileSystem.Primitives", version: pkgVersion },
    { id: "System.IO.FileSystem.Watcher", version: pkgVersion },
    { id: "System.IO.IsolatedStorage", version: pkgVersion },
    { id: "System.IO.MemoryMappedFiles", version: pkgVersion },
    { id: "System.IO.Pipes", version: pkgVersion },
    { id: "System.IO.Pipes.AccessControl", version: pkgVersion },
    { id: "System.IO.UnmanagedMemoryStream", version: pkgVersion },
    { id: "System.Linq", version: pkgVersion },
    { id: "System.Linq.Expressions", version: pkgVersion },
    { id: "System.Linq.Parallel", version: pkgVersion },
    { id: "System.Linq.Queryable", version: pkgVersion },
    { id: "System.Net.Http", version: pkgVersion },
    { id: "System.Net.NameResolution", version: pkgVersion },
    { id: "System.Net.NetworkInformation", version: pkgVersion },
    { id: "System.Net.Ping", version: pkgVersion },
    { id: "System.Net.Primitives", version: pkgVersion },
    { id: "System.Net.Requests", version: pkgVersion },
    { id: "System.Net.Security", version: "4.3.1" },
    { id: "System.Net.Sockets", version: pkgVersion },
    { id: "System.Net.WebHeaderCollection", version: pkgVersion },
    { id: "System.Net.WebSockets", version: pkgVersion },
    { id: "System.Net.WebSockets.Client", version: "4.3.1" },
    { id: "System.ObjectModel", version: pkgVersion },
    { id: "System.Private.DataContractSerialization", version: pkgVersion },
    { id: "System.Reflection", version: pkgVersion },
    { id: "System.Reflection.DispatchProxy", version: pkgVersion },
    { id: "System.Reflection.Emit", version: pkgVersion },
    { id: "System.Reflection.Emit.ILGeneration", version: pkgVersion },
    { id: "System.Reflection.Emit.Lightweight", version: pkgVersion },
    { id: "System.Reflection.Extensions", version: pkgVersion },
    { id: "System.Reflection.Primitives", version: pkgVersion },
    { id: "System.Reflection.TypeExtensions", version: pkgVersion },
    { id: "System.Resources.Reader", version: pkgVersion },
    { id: "System.Resources.ResourceManager", version: pkgVersion },
    { id: "System.Resources.Writer", version: pkgVersion },
    { id: "System.Runtime", version: pkgVersion },
    { id: "System.Runtime.CompilerServices.VisualC", version: pkgVersion },
    { id: "System.Runtime.Extensions", version: pkgVersion },
    { id: "System.Runtime.Handles", version: pkgVersion },
    { id: "System.Runtime.InteropServices", version: pkgVersion },
    { id: "System.Runtime.InteropServices.RuntimeInformation", version: pkgVersion },
    { id: "System.Runtime.InteropServices.WindowsRuntime", version: pkgVersion },
    { id: "System.Runtime.Loader", version: pkgVersion },
    { id: "System.Runtime.Numerics", version: pkgVersion },
    { id: "System.Runtime.Serialization.Formatters", version: pkgVersion },
    { id: "System.Runtime.Serialization.Json", version: pkgVersion },
    { id: "System.Runtime.Serialization.Primitives", version: pkgVersion },
    { id: "System.Runtime.Serialization.Xml", version: pkgVersion },
    { id: "System.Runtime.WindowsRuntime", version: pkgVersion },
    { id: "System.Security.Cryptography.Algorithms", version: pkgVersion },
    { id: "System.Security.Cryptography.Cng", version: pkgVersion },
    { id: "System.Security.Cryptography.Csp", version: pkgVersion },
    { id: "System.Security.Cryptography.Encoding", version: pkgVersion },
    { id: "System.Security.Cryptography.Primitives", version: pkgVersion },
    { id: "System.Security.Cryptography.X509Certificates", version: pkgVersion },
    { id: "System.Security.Principal", version: pkgVersion },
    { id: "System.Security.SecureString", version: pkgVersion },
    { id: "System.Security.Claims", version: pkgVersion },
    { id: "System.Text.Encoding", version: pkgVersion },
    { id: "System.Text.Encoding.Extensions", version: pkgVersion },
    { id: "System.Text.RegularExpressions", version: pkgVersion },
    { id: "System.Threading", version: pkgVersion },
    { id: "System.Threading.Overlapped", version: pkgVersion },
    { id: "System.Threading.Tasks", version: pkgVersion },

    { id: "System.Threading.Tasks.Parallel", version: pkgVersion },
    { id: "System.Threading.Thread", version: pkgVersion },
    { id: "System.Threading.ThreadPool", version: pkgVersion },
    { id: "System.Threading.Timer", version: pkgVersion },
    { id: "System.ValueTuple", version: pkgVersion },
    { id: "System.Xml.ReaderWriter", version: pkgVersion },
    { id: "System.Xml.XDocument", version: pkgVersion },
    { id: "System.Xml.XmlDocument", version: pkgVersion },
    { id: "System.Xml.XmlSerializer", version: pkgVersion },
    { id: "System.Xml.XPath", version: pkgVersion },
    { id: "System.Xml.XPath.XDocument", version: pkgVersion },
    { id: "System.Xml.XPath.XmlDocument", version: pkgVersion },

    // Bumped version numbers
    { id: "System.IO.FileSystem.AccessControl", version: pkgVersionNext },
    { id: "System.Management", version: pkgVersionNext },
    { id: "System.Security.AccessControl", version: pkgVersionNext,
        dependentPackageIdsToSkip: ["System.Security.Principal.Windows"] },
    { id: "System.Security.Principal.Windows", version: pkgVersionNext },
    { id: "System.Text.Json", version: pkgVersionNext,
        dependentPackageIdsToSkip: ["System.Memory", "System.ValueTuple", "System.Runtime.CompilerServices.Unsafe", "System.Numerics.Vectors", "System.Threading.Tasks.Extensions", "Microsoft.Bcl.AsyncInterfaces"],
    },
    { id: "System.Threading.AccessControl", version: pkgVersionNext },

    // Non-standard version ones
    { id: "Microsoft.NETCore.Targets", version: "2.0.0" },
    
    // NOTE(jubayard): If you depend on this package and need to build for Net472, you will need to add the
    // dependency manually, using netstandard2.0 targetFramework qualifier. Dependency clipped because it 
    // causes a deployment conflict for the cache.
    { id: "System.Threading.Tasks.Extensions", version: "4.5.2",
        dependentPackageIdsToSkip: ["System.Runtime.CompilerServices.Unsafe"] },

    { id: "System.Security.Cryptography.OpenSsl", version: "4.4.0" },
    { id: "System.Collections.Immutable", version: "1.5.0" },
];






