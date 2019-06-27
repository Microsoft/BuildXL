// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using BuildXL.Ipc;
using BuildXL.Ipc.Interfaces;
using BuildXL.Utilities.CLI;
using Tool.ServicePipDaemon;
using static Tool.ServicePipDaemon.Statics;

namespace Tool.DropDaemon
{
    /// <summary>
    /// DropDaemon entry point.
    /// </summary>
    public static class Program
    {
        /// <nodoc/>
        [SuppressMessage("Microsoft.Naming", "CA2204:Spelling of DropD")]
        public static int Main(string[] args)
        {
            // TODO:# 1208464- this can be removed once DropDaemon targets .net or newer 4.7 where TLS 1.2 is enabled by default
            ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol | SecurityProtocolType.Tls12;

            if (args.Length > 0 && args[0] == "listen")
            {
                return ServicePipDaemon.ServicePipDaemon.SubscribeAndProcessCloudBuildEvents();
            }

            try
            {
                Console.WriteLine("DropDaemon started at " + DateTime.UtcNow);
                Console.WriteLine(DropDaemon.DropDLogPrefix + "Command line arguments: ");
                Console.WriteLine(string.Join(Environment.NewLine + DropDaemon.DropDLogPrefix, args));
                Console.WriteLine();

                ConfiguredCommand conf = ServicePipDaemon.ServicePipDaemon.ParseArgs(args, new UnixParser());
                if (conf.Command.NeedsIpcClient)
                {
                    using (var rpc = CreateClient(conf))
                    {
                        var result = conf.Command.ClientAction(conf, rpc);
                        rpc.RequestStop();
                        rpc.Completion.GetAwaiter().GetResult();
                        return result;
                    }
                }
                else
                {
                    return conf.Command.ClientAction(conf, null);
                }
            }
            catch (ArgumentException e)
            {
                Error(e.Message);
                return 3;
            }
        }

        internal static IClient CreateClient(ConfiguredCommand conf)
        {
            var daemonConfig = ServicePipDaemon.ServicePipDaemon.CreateDaemonConfig(conf);
            return IpcFactory.GetProvider().GetClient(daemonConfig.Moniker, daemonConfig);
        }
    }
}
