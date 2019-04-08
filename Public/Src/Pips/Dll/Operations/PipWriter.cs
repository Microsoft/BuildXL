// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Utilities;

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// An extended binary writer that can write Pips
    /// </summary>
    /// <remarks>
    /// This type is internal, as the serialization/deserialization functionality is encapsulated by the PipTable.
    /// </remarks>
    internal sealed class PipWriter : BuildXLWriter
    {
        public PipWriter(PageableStore store, Stream stream, bool leaveOpen, bool logStats)
            : base(store.Debug, stream, leaveOpen, logStats)
        {
            Contract.Requires(store != null);
            Store = store;
        }

        public PageableStore Store { get; }

        public void Write(Pip pip)
        {
            Contract.Requires(pip != null);
            Start<Pip>();
            pip.Serialize(this);
            End();
        }

        public void Write(in PipData value)
        {
            Start<PipData>();
            value.Serialize(this);
            End();
        }

        public void Write(in EnvironmentVariable value)
        {
            Start<EnvironmentVariable>();
            value.Serialize(this);
            End();
        }

        public void Write(RegexDescriptor value)
        {
            Start<RegexDescriptor>();
            value.Serialize(this);
            End();
        }

        public void Write(PipProvenance value)
        {
            Contract.Requires(value != null);
            Start<PipProvenance>();
            value.Serialize(this);
            End();
        }

        public void Write(PipId value)
        {
            Start<PipId>();
            Write(value.Value);
            End();
        }

        public void Write(in ProcessSemaphoreInfo value)
        {
            Contract.Requires(value != null);
            Start<ProcessSemaphoreInfo>();
            value.Serialize(this);
            End();
        }
    }
}
